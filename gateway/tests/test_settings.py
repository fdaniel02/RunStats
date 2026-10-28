import pytest

from garmin_gateway.settings import Settings


def test_defaults():
    s = Settings.from_env({})

    assert s.email is None
    assert s.password is None
    assert s.token_dir == "/data"
    assert s.min_seconds_between_calls == 1
    assert s.rate_limit_cooldown_seconds == 300
    assert s.login_failure_cooldown_seconds == 900


def test_values_from_the_environment():
    s = Settings.from_env(
        {
            "GARMIN_EMAIL": "runner@example.com",
            "GARMIN_PASSWORD": "secret",
            "GARMIN_TOKEN_DIR": "/var/lib/garmin-tokens",
            "GARMIN_MIN_SECONDS_BETWEEN_CALLS": "0.5",
            "GARMIN_RATE_LIMIT_COOLDOWN_SECONDS": "60",
            "GARMIN_LOGIN_FAILURE_COOLDOWN_SECONDS": "0",
        }
    )

    assert (s.email, s.password, s.token_dir) == ("runner@example.com", "secret", "/var/lib/garmin-tokens")
    assert (s.min_seconds_between_calls, s.rate_limit_cooldown_seconds, s.login_failure_cooldown_seconds) == (
        0.5,
        60,
        0,
    )


def test_empty_credentials_count_as_missing():
    s = Settings.from_env({"GARMIN_EMAIL": "", "GARMIN_PASSWORD": ""})

    assert s.email is None
    assert s.password is None


@pytest.mark.parametrize(("value", "message"), [("soon", "must be a number"), ("-1", "must not be negative")])
def test_invalid_seconds_name_the_setting(value, message):
    with pytest.raises(ValueError, match=f"GARMIN_MIN_SECONDS_BETWEEN_CALLS {message}"):
        Settings.from_env({"GARMIN_MIN_SECONDS_BETWEEN_CALLS": value})
