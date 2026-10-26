import json
import logging
import sys

import pytest
from opentelemetry.sdk.trace import TracerProvider

from garmin_gateway.logs import configure_logging


@pytest.fixture
def restore_logging():
    root = logging.getLogger()
    handlers, level = root.handlers[:], root.level
    yield
    root.handlers[:] = handlers
    root.setLevel(level)
    logging.getLogger("uvicorn.access").filters.clear()


def lines(capsys):
    return [json.loads(line) for line in capsys.readouterr().out.splitlines()]


def test_logs_are_json_with_the_same_fields_as_the_app(restore_logging, capsys):
    configure_logging()

    logging.getLogger("garmin_gateway.garmin").warning("Rate limit %s", "reached")

    [entry] = lines(capsys)
    assert entry["LogLevel"] == "WARNING"
    assert entry["Category"] == "garmin_gateway.garmin"
    assert entry["Message"] == "Rate limit reached"
    assert entry["Timestamp"].endswith("Z")
    assert len(entry["Timestamp"]) == len("2026-10-26T13:55:21.517Z")


def test_health_check_requests_are_not_logged(restore_logging, capsys):
    configure_logging()
    access = logging.getLogger("uvicorn.access")

    access.info('%s - "%s %s HTTP/%s" %d', "10.42.0.1:5000", "GET", "/healthz/live", "1.1", 200)
    access.info('%s - "%s %s HTTP/%s" %d', "10.42.0.1:5000", "GET", "/activities", "1.1", 200)

    [entry] = lines(capsys)
    assert "/activities" in entry["Message"]


def test_debug_messages_are_left_out(restore_logging, capsys):
    configure_logging()

    logging.getLogger("garminconnect").debug("token details")

    assert lines(capsys) == []


def test_errors_include_the_exception(restore_logging, capsys):
    configure_logging()

    try:
        raise RuntimeError("boom")
    except RuntimeError:
        logging.getLogger("garmin_gateway").exception("Failed")

    [entry] = lines(capsys)
    assert "RuntimeError: boom" in entry["exc_info"]


def test_writes_to_stdout(restore_logging):
    configure_logging()

    assert logging.getLogger().handlers[0].stream is sys.stdout


def test_uvicorns_colored_copy_of_the_message_is_dropped(restore_logging, capsys):
    configure_logging()

    logging.getLogger("uvicorn.error").info("Started", extra={"color_message": "Started in color"})

    [entry] = lines(capsys)
    assert entry["Message"] == "Started"
    assert "color_message" not in entry


def test_log_lines_carry_the_current_trace(restore_logging, capsys):
    configure_logging()
    tracer = TracerProvider().get_tracer("test")

    with tracer.start_as_current_span("request") as span:
        logging.getLogger("garmin_gateway").info("Inside")
    logging.getLogger("garmin_gateway").info("Outside")

    inside, outside = lines(capsys)
    assert inside["TraceId"] == format(span.get_span_context().trace_id, "032x")
    assert inside["SpanId"] == format(span.get_span_context().span_id, "016x")
    assert "TraceId" not in outside
