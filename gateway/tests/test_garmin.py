import logging

import pytest
from fakes import FakeGarmin, FakeTime, settings
from garminconnect import (
    GarminConnectAuthenticationError,
    GarminConnectConnectionError,
    GarminConnectTooManyRequestsError,
)

from garmin_gateway.garmin import GarminService, GarminUnavailable


@pytest.fixture
def garmin():
    return FakeGarmin()


@pytest.fixture
def time():
    return FakeTime()


@pytest.fixture
def token_dir(tmp_path):
    return str(tmp_path / "tokens")


def service(garmin, time, token_dir, **overrides):
    return GarminService(settings(token_dir, **overrides), garmin.factory, time.clock, time.sleep)


def test_logs_in_on_the_first_call_with_the_token_dir(garmin, time, token_dir, tmp_path):
    garmin.responses["get_activities"] = [{"activityId": 1}]
    s = service(garmin, time, token_dir)

    assert s.call(lambda g: g.get_activities(0, 10)) == [{"activityId": 1}]

    assert garmin.logins == [("runner@example.com", "secret", token_dir)]
    assert (tmp_path / "tokens").is_dir()
    assert s.logged_in


def test_later_calls_reuse_the_session(garmin, time, token_dir):
    s = service(garmin, time, token_dir)

    s.call(lambda g: g.get_activity("1"))
    s.call(lambda g: g.get_activity("2"))

    assert len(garmin.logins) == 1


def test_login_reports_whether_a_saved_token_was_used(garmin, time, token_dir, tmp_path, caplog):
    (tmp_path / "tokens").mkdir()
    (tmp_path / "tokens" / "garmin_tokens.json").write_text("{}")
    caplog.set_level(logging.INFO)

    service(garmin, time, token_dir).login()

    assert "Logged in to Garmin Connect (saved token)" in caplog.messages


def test_login_without_a_token_says_it_used_the_password(garmin, time, token_dir, caplog):
    caplog.set_level(logging.INFO)

    service(garmin, time, token_dir).login()

    assert "Logged in to Garmin Connect (email and password)" in caplog.messages
    assert not any("secret" in m for m in caplog.messages)


def test_login_when_already_logged_in_does_nothing(garmin, time, token_dir):
    s = service(garmin, time, token_dir)
    s.login()

    s.login()

    assert len(garmin.logins) == 1


def test_calls_are_spaced_out(garmin, time, token_dir):
    s = service(garmin, time, token_dir, min_seconds_between_calls=1.0)
    s.login()
    time.now += 0.25

    s.call(lambda g: g.get_activity("1"))

    assert time.sleeps == [0.75]


def test_no_wait_when_enough_time_has_passed(garmin, time, token_dir):
    s = service(garmin, time, token_dir)
    s.login()
    time.now += 5

    s.call(lambda g: g.get_activity("1"))

    assert time.sleeps == []


def test_rejected_session_logs_in_again_and_retries_once(garmin, time, token_dir):
    garmin.responses["get_activity"] = {"activityId": 1}
    s = service(garmin, time, token_dir)
    s.login()
    garmin.call_errors = [GarminConnectAuthenticationError("401")]

    assert s.call(lambda g: g.get_activity("1")) == {"activityId": 1}

    assert len(garmin.logins) == 2
    assert [c[0] for c in garmin.calls] == ["get_activity", "get_activity"]


def test_session_still_rejected_after_a_new_login_fails_the_call(garmin, time, token_dir):
    s = service(garmin, time, token_dir)
    garmin.call_errors = [GarminConnectAuthenticationError("401"), GarminConnectAuthenticationError("401")]

    with pytest.raises(GarminConnectAuthenticationError):
        s.call(lambda g: g.get_activity("1"))


def test_failed_login_blocks_further_logins_for_the_cooldown(garmin, time, token_dir):
    s = service(garmin, time, token_dir, login_failure_cooldown_seconds=900)
    garmin.login_errors = [GarminConnectAuthenticationError("bad password")]

    with pytest.raises(GarminUnavailable) as first:
        s.call(lambda g: g.get_activity("1"))
    time.now += 600
    with pytest.raises(GarminUnavailable) as second:
        s.call(lambda g: g.get_activity("1"))

    assert first.value.reason == "Garmin login failed"
    assert first.value.retry_after_seconds == 900
    assert second.value.retry_after_seconds == 300
    assert len(garmin.logins) == 1
    assert not s.logged_in


def test_login_is_tried_again_after_the_cooldown(garmin, time, token_dir):
    s = service(garmin, time, token_dir, login_failure_cooldown_seconds=900)
    garmin.login_errors = [GarminConnectAuthenticationError("bad password")]
    with pytest.raises(GarminUnavailable):
        s.login()
    time.now += 900

    s.login()

    assert s.logged_in
    assert len(garmin.logins) == 2


def test_rate_limit_blocks_calls_for_the_cooldown(garmin, time, token_dir, caplog):
    s = service(garmin, time, token_dir, rate_limit_cooldown_seconds=300)
    s.login()
    garmin.call_errors = [GarminConnectTooManyRequestsError("429")]

    with pytest.raises(GarminUnavailable) as first:
        s.call(lambda g: g.get_activity("1"))
    time.now += 100.5
    with pytest.raises(GarminUnavailable) as second:
        s.call(lambda g: g.get_activity("2"))

    assert first.value.reason == "Garmin rate limit reached"
    assert first.value.retry_after_seconds == 300
    assert second.value.retry_after_seconds == 200
    assert len(garmin.calls) == 1
    assert any("rate limit" in m for m in caplog.messages)


def test_rate_limit_during_login_blocks_too(garmin, time, token_dir):
    s = service(garmin, time, token_dir)
    garmin.login_errors = [GarminConnectTooManyRequestsError("427")]

    with pytest.raises(GarminUnavailable) as error:
        s.login()

    assert error.value.reason == "Garmin rate limit reached"
    assert not s.logged_in


def test_other_errors_pass_through_without_blocking(garmin, time, token_dir):
    s = service(garmin, time, token_dir)
    garmin.call_errors = [GarminConnectConnectionError("500")]

    with pytest.raises(GarminConnectConnectionError):
        s.call(lambda g: g.get_activity("1"))
    s.call(lambda g: g.get_activity("1"))

    assert len(garmin.calls) == 2
