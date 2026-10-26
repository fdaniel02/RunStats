import logging
import math
import os
import threading
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

from garminconnect import Garmin, GarminConnectAuthenticationError, GarminConnectTooManyRequestsError

from .settings import Settings

logger = logging.getLogger(__name__)


class GarminUnavailable(Exception):
    """Garmin isn't called for a while, after a rate limit or a failed login."""

    def __init__(self, reason: str, retry_after_seconds: float) -> None:
        super().__init__(reason)
        self.reason = reason
        self.retry_after_seconds = retry_after_seconds


class GarminService:
    """Runs every Garmin call one at a time, logged in and throttled."""

    def __init__(
        self,
        settings: Settings,
        client_factory: Callable[[str | None, str | None], Any] = Garmin,
        clock: Callable[[], float] = time.monotonic,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        self._settings = settings
        self._client_factory = client_factory
        self._clock = clock
        self._sleep = sleep
        self._lock = threading.Lock()
        self._client: Any = None
        self._last_call_end: float | None = None
        self._blocked_until = 0.0
        self._blocked_reason = ""

    @property
    def logged_in(self) -> bool:
        return self._client is not None

    def login(self) -> None:
        with self._lock:
            self._check_blocked()
            if self._client is None:
                self._login()

    def call[T](self, operation: Callable[[Any], T]) -> T:
        with self._lock:
            self._check_blocked()
            client = self._client or self._login()
            try:
                return self._throttled(lambda: operation(client))
            except GarminConnectAuthenticationError:
                # The library already tried to refresh the token; a new login is what's left.
                logger.warning("Garmin rejected the session, logging in again")
                self._client = None
                client = self._login()
                return self._throttled(lambda: operation(client))

    def _login(self) -> Any:
        token_dir = self._settings.token_dir
        os.makedirs(token_dir, exist_ok=True)
        had_token = (Path(token_dir) / "garmin_tokens.json").exists()
        client = self._client_factory(self._settings.email, self._settings.password)
        try:
            self._throttled(lambda: client.login(token_dir))
        except GarminConnectAuthenticationError as e:
            logger.exception("Garmin login failed")
            self._block(self._settings.login_failure_cooldown_seconds, "Garmin login failed")
            raise self._unavailable() from e
        self._client = client
        logger.info("Logged in to Garmin Connect (%s)", "saved token" if had_token else "email and password")
        return client

    def _throttled[T](self, action: Callable[[], T]) -> T:
        if self._last_call_end is not None:
            wait = self._last_call_end + self._settings.min_seconds_between_calls - self._clock()
            if wait > 0:
                self._sleep(wait)
        try:
            return action()
        except GarminConnectTooManyRequestsError as e:
            logger.warning("Garmin rate limit reached: %s", e)
            self._block(self._settings.rate_limit_cooldown_seconds, "Garmin rate limit reached")
            raise self._unavailable() from e
        finally:
            self._last_call_end = self._clock()

    def _block(self, seconds: float, reason: str) -> None:
        self._blocked_until = self._clock() + seconds
        self._blocked_reason = reason

    def _check_blocked(self) -> None:
        if self._clock() < self._blocked_until:
            raise self._unavailable()

    def _unavailable(self) -> GarminUnavailable:
        return GarminUnavailable(self._blocked_reason, math.ceil(self._blocked_until - self._clock()))
