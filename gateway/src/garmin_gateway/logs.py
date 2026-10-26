import logging
import sys
from datetime import UTC, datetime
from typing import Any

from opentelemetry import trace
from pythonjsonlogger.json import JsonFormatter


class _Formatter(JsonFormatter):
    # Same field names and timestamp format as the .NET app's JSON console logs.
    def formatTime(self, record: logging.LogRecord, datefmt: str | None = None) -> str:
        return datetime.fromtimestamp(record.created, UTC).isoformat(timespec="milliseconds").replace("+00:00", "Z")

    def add_fields(self, log_data: dict[str, Any], record: logging.LogRecord, message_dict: dict[str, Any]) -> None:
        super().add_fields(log_data, record, message_dict)
        # uvicorn's copy of the message with terminal color codes.
        log_data.pop("color_message", None)
        span = trace.get_current_span().get_span_context()
        if span.is_valid:
            log_data["TraceId"] = format(span.trace_id, "032x")
            log_data["SpanId"] = format(span.span_id, "016x")


class _SkipHealthChecks(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        # uvicorn's access log arguments: client address, method, path, HTTP version, status.
        args = record.args
        return not (isinstance(args, tuple) and len(args) > 2 and str(args[2]).startswith("/healthz"))


def configure_logging(level: int = logging.INFO) -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(
        _Formatter(
            "%(asctime)s %(levelname)s %(name)s %(message)s",
            rename_fields={"asctime": "Timestamp", "levelname": "LogLevel", "name": "Category", "message": "Message"},
        )
    )
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(level)
    for name in ("uvicorn", "uvicorn.error", "uvicorn.access"):
        logging.getLogger(name).handlers.clear()
        logging.getLogger(name).propagate = True
    logging.getLogger("uvicorn.access").addFilter(_SkipHealthChecks())
