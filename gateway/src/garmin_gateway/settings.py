import os
from collections.abc import Mapping
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    email: str | None
    password: str | None
    token_dir: str
    min_seconds_between_calls: float
    rate_limit_cooldown_seconds: float
    # Repeated failed logins can get the Garmin account locked, so a failed login isn't retried right away.
    login_failure_cooldown_seconds: float

    @classmethod
    def from_env(cls, env: Mapping[str, str] = os.environ) -> Settings:
        return cls(
            email=env.get("GARMIN_EMAIL") or None,
            password=env.get("GARMIN_PASSWORD") or None,
            token_dir=env.get("GARMIN_TOKEN_DIR", "/data"),
            min_seconds_between_calls=_seconds(env, "GARMIN_MIN_SECONDS_BETWEEN_CALLS", 1),
            rate_limit_cooldown_seconds=_seconds(env, "GARMIN_RATE_LIMIT_COOLDOWN_SECONDS", 300),
            login_failure_cooldown_seconds=_seconds(env, "GARMIN_LOGIN_FAILURE_COOLDOWN_SECONDS", 900),
        )


def _seconds(env: Mapping[str, str], name: str, default: float) -> float:
    raw = env.get(name)
    if raw is None:
        return default
    try:
        value = float(raw)
    except ValueError:
        raise ValueError(f"Setting {name} must be a number of seconds, got {raw!r}") from None
    if value < 0:
        raise ValueError(f"Setting {name} must not be negative, got {raw!r}")
    return value
