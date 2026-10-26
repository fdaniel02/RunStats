import io
import zipfile

import pytest
from fakes import FakeGarmin, FakeTime, settings
from fastapi.testclient import TestClient
from garminconnect import (
    Garmin,
    GarminConnectAuthenticationError,
    GarminConnectConnectionError,
    GarminConnectNotFoundError,
    GarminConnectTooManyRequestsError,
)

from garmin_gateway.app import create_app
from garmin_gateway.garmin import GarminService


@pytest.fixture
def garmin():
    return FakeGarmin()


@pytest.fixture
def client(garmin, tmp_path):
    time = FakeTime()
    service = GarminService(settings(str(tmp_path)), garmin.factory, time.clock, time.sleep)
    with TestClient(create_app(service)) as test_client:
        test_client.app.state.login.join()
        yield test_client


def zip_of(**files: bytes) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as z:
        for name, content in files.items():
            z.writestr(name, content)
    return buffer.getvalue()


def test_activities_passes_the_page(client, garmin):
    garmin.responses["get_activities"] = [{"activityId": 7}]

    response = client.get("/activities", params={"start": 20, "limit": 50})

    assert response.status_code == 200
    assert response.json() == [{"activityId": 7}]
    assert garmin.calls[-1] == ("get_activities", (20, 50), {})


def test_activities_defaults_to_the_newest_hundred(client, garmin):
    client.get("/activities")

    assert garmin.calls[-1] == ("get_activities", (0, 100), {})


@pytest.mark.parametrize("params", [{"limit": 0}, {"limit": 1001}, {"start": -1}])
def test_activities_rejects_out_of_range_paging(client, params):
    assert client.get("/activities", params=params).status_code == 422


@pytest.mark.parametrize(
    ("path", "method"),
    [
        ("/activities/42", "get_activity"),
        ("/activities/42/splits", "get_activity_splits"),
        ("/activities/42/hr-zones", "get_activity_hr_in_timezones"),
        ("/activities/42/weather", "get_activity_weather"),
    ],
)
def test_activity_endpoints_return_garmins_json(client, garmin, path, method):
    garmin.responses[method] = {"from": method}

    response = client.get(path)

    assert response.status_code == 200
    assert response.json() == {"from": method}
    assert garmin.calls[-1] == (method, ("42",), {})


@pytest.mark.parametrize("path", ["/activities/0", "/activities/-3/splits", "/activities/abc/fit"])
def test_activity_id_must_be_positive(client, path):
    assert client.get(path).status_code == 422


def test_personal_records(client, garmin):
    garmin.responses["get_personal_record"] = [{"typeId": 3, "value": 1500.0}]

    response = client.get("/personal-records")

    assert response.json() == [{"typeId": 3, "value": 1500.0}]


def test_fit_is_unpacked_from_the_original_download(client, garmin):
    garmin.responses["download_activity"] = zip_of(**{"42_ACTIVITY.fit": b"FIT-DATA"})

    response = client.get("/activities/42/fit")

    assert response.status_code == 200
    assert response.content == b"FIT-DATA"
    assert response.headers["content-type"] == "application/vnd.ant.fit"
    assert garmin.calls[-1] == ("download_activity", ("42",), {"dl_fmt": Garmin.ActivityDownloadFormat.ORIGINAL})


def test_imported_activity_without_a_fit_file_is_not_found(client, garmin):
    garmin.responses["download_activity"] = zip_of(**{"42.gpx": b"<gpx/>"})

    response = client.get("/activities/42/fit")

    assert response.status_code == 404
    assert "found 0" in response.json()["error"]


def test_unknown_activity_is_not_found(client, garmin):
    garmin.call_errors = [GarminConnectNotFoundError("404")]

    response = client.get("/activities/42")

    assert response.status_code == 404


def test_rate_limit_is_service_unavailable_with_retry_after(client, garmin):
    garmin.call_errors = [GarminConnectTooManyRequestsError("429")]

    response = client.get("/activities/42")

    assert response.status_code == 503
    assert response.headers["retry-after"] == "300"
    assert response.json() == {"error": "Garmin rate limit reached"}


def test_other_garmin_failures_are_bad_gateway(client, garmin, caplog):
    garmin.call_errors = [GarminConnectConnectionError("500 Server Error")]

    response = client.get("/activities/42")

    assert response.status_code == 502
    assert response.json() == {"error": "Garmin Connect request failed"}
    assert any("/activities/42" in m for m in caplog.messages)


def test_live_does_not_depend_on_garmin(client):
    response = client.get("/healthz/live")

    assert (response.status_code, response.text) == (200, "Healthy")


def test_ready_once_logged_in(client):
    client.get("/activities")

    response = client.get("/healthz/ready")

    assert (response.status_code, response.text) == (200, "Healthy")


def test_not_ready_while_login_fails(garmin, tmp_path):
    garmin.login_errors = [GarminConnectAuthenticationError("bad password")]
    time = FakeTime()
    service = GarminService(settings(str(tmp_path)), garmin.factory, time.clock, time.sleep)

    with TestClient(create_app(service)) as test_client:
        test_client.app.state.login.join()
        response = test_client.get("/healthz/ready")

    assert (response.status_code, response.text) == (503, "Unhealthy")


def test_starts_logging_in_at_startup(garmin, tmp_path):
    time = FakeTime()
    service = GarminService(settings(str(tmp_path)), garmin.factory, time.clock, time.sleep)

    with TestClient(create_app(service)) as test_client:
        test_client.app.state.login.join()

    assert len(garmin.logins) == 1


def test_unexpected_startup_login_error_is_logged(garmin, tmp_path, caplog):
    garmin.login_errors = [GarminConnectConnectionError("Garmin SSO unreachable")]
    time = FakeTime()
    service = GarminService(settings(str(tmp_path)), garmin.factory, time.clock, time.sleep)

    with TestClient(create_app(service)) as test_client:
        test_client.app.state.login.join()

    assert "Garmin login failed" in caplog.messages
    assert not service.logged_in
