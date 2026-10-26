import logging
import math
import os
import threading
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

from garminconnect import (
    Garmin,
    GarminConnectAuthenticationError,
    GarminConnectNotFoundError,
    GarminConnectTooManyRequestsError,
)
from opentelemetry.trace import SpanKind, Status, StatusCode

from .settings import Settings
from .telemetry import GarminTelemetry

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
        telemetry: GarminTelemetry | None = None,
    ) -> None:
        self._settings = settings
        self._telemetry = telemetry or GarminTelemetry()
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

    def call[T](self, name: str, operation: Callable[[Any], T]) -> T:
        with self._lock:
            self._check_blocked()
            client = self._client or self._login()
            try:
                return self._throttled(name, lambda: operation(client))
            except GarminConnectAuthenticationError:
                # The library already tried to refresh the token; a new login is what's left.
                logger.warning("Garmin rejected the session, logging in again")
                self._client = None
                client = self._login()
                return self._throttled(name, lambda: operation(client))

    def _login(self) -> Any:
        token_dir = self._settings.token_dir
        os.makedirs(token_dir, exist_ok=True)
        had_token = (Path(token_dir) / "garmin_tokens.json").exists()
        client = self._client_factory(self._settings.email, self._settings.password)
        outcome = "error"
        try:
            self._throttled("login", lambda: client.login(token_dir))
            outcome = "ok"
        except GarminConnectAuthenticationError as e:
            outcome = "failed"
            logger.exception("Garmin login failed")
            self._block(self._settings.login_failure_cooldown_seconds, "Garmin login failed")
            raise self._unavailable() from e
        except GarminUnavailable:
            outcome = "rate_limited"
            raise
        finally:
            method = "saved_token" if had_token else "password"
            self._telemetry.logins.add(1, {"garmin.login.method": method, "garmin.outcome": outcome})
        self._client = client
        logger.info("Logged in to Garmin Connect (%s)", "saved token" if had_token else "email and password")
        return client

    def _throttled[T](self, name: str, action: Callable[[], T]) -> T:
        wait = 0.0
        if self._last_call_end is not None:
            wait = max(0.0, self._last_call_end + self._settings.min_seconds_between_calls - self._clock())
            if wait > 0:
                self._sleep(wait)
        # Exceptions aren't recorded on the span: their messages can contain Garmin URLs with the user's name.
        with self._telemetry.tracer.start_as_current_span(
            f"Garmin {name}", kind=SpanKind.CLIENT, record_exception=False, set_status_on_exception=False
        ) as span:
            span.set_attribute("garmin.operation", name)
            span.set_attribute("garmin.throttle_wait_seconds", wait)
            start = self._clock()
            outcome = "error"
            try:
                result = action()
                outcome = "ok"
                return result
            except GarminConnectNotFoundError:
                outcome = "not_found"
                raise
            except GarminConnectAuthenticationError:
                outcome = "unauthorized"
                raise
            except GarminConnectTooManyRequestsError as e:
                outcome = "rate_limited"
                self._telemetry.rate_limits.add(1, {"garmin.operation": name})
                logger.warning("Garmin rate limit reached: %s", e)
                self._block(self._settings.rate_limit_cooldown_seconds, "Garmin rate limit reached")
                raise self._unavailable() from e
            finally:
                self._last_call_end = self._clock()
                attributes = {"garmin.operation": name, "garmin.outcome": outcome}
                span.set_attribute("garmin.outcome", outcome)
                if outcome not in ("ok", "not_found"):
                    span.set_status(Status(StatusCode.ERROR))
                self._telemetry.calls.add(1, attributes)
                self._telemetry.call_duration.record(self._last_call_end - start, attributes)

    def _block(self, seconds: float, reason: str) -> None:
        self._blocked_until = self._clock() + seconds
        self._blocked_reason = reason

    def _check_blocked(self) -> None:
        if self._clock() < self._blocked_until:
            raise self._unavailable()

    def _unavailable(self) -> GarminUnavailable:
        return GarminUnavailable(self._blocked_reason, math.ceil(self._blocked_until - self._clock()))
