from collections.abc import Callable
from typing import Any

from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader
from opentelemetry.sdk.trace import ReadableSpan, TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

from garmin_gateway.settings import Settings
from garmin_gateway.telemetry import GarminTelemetry


def settings(token_dir: str, **overrides: Any) -> Settings:
    values: dict[str, Any] = {
        "email": "runner@example.com",
        "password": "secret",
        "token_dir": token_dir,
        "min_seconds_between_calls": 1.0,
        "rate_limit_cooldown_seconds": 300.0,
        "login_failure_cooldown_seconds": 900.0,
    }
    values.update(overrides)
    return Settings(**values)


class FakeTime:
    def __init__(self) -> None:
        self.now = 1000.0
        self.sleeps: list[float] = []

    def clock(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.sleeps.append(seconds)
        self.now += seconds


class FakeGarmin:
    """Stands in for garminconnect.Garmin; every client a factory creates shares the same script."""

    def __init__(self) -> None:
        self.logins: list[tuple[str | None, str | None, str]] = []
        self.login_errors: list[Exception] = []
        self.call_errors: list[Exception] = []
        self.responses: dict[str, Any] = {}
        self.calls: list[tuple[str, tuple[Any, ...], dict[str, Any]]] = []

    def factory(self, email: str | None, password: str | None) -> Client:
        return Client(self, email, password)


class Client:
    def __init__(self, script: FakeGarmin, email: str | None, password: str | None) -> None:
        self._script = script
        self._email = email
        self._password = password

    def login(self, tokenstore: str) -> None:
        self._script.logins.append((self._email, self._password, tokenstore))
        if self._script.login_errors:
            raise self._script.login_errors.pop(0)

    def __getattr__(self, name: str) -> Callable[..., Any]:
        def method(*args: Any, **kwargs: Any) -> Any:
            self._script.calls.append((name, args, kwargs))
            if self._script.call_errors:
                raise self._script.call_errors.pop(0)
            return self._script.responses.get(name)

        return method


class CollectedTelemetry:
    """Real OpenTelemetry providers that keep spans and metrics in memory instead of exporting them."""

    def __init__(self) -> None:
        self.spans = InMemorySpanExporter()
        self.tracer_provider = TracerProvider()
        self.tracer_provider.add_span_processor(SimpleSpanProcessor(self.spans))
        self.reader = InMemoryMetricReader()
        self.meter_provider = MeterProvider(metric_readers=[self.reader])
        self.garmin = GarminTelemetry(self.tracer_provider, self.meter_provider)

    def finished_spans(self) -> list[ReadableSpan]:
        return list(self.spans.get_finished_spans())

    def points(self, name: str) -> dict[frozenset[tuple[str, Any]], Any]:
        """Data points of one metric by their attributes: the count for counters, the data point for histograms."""
        data = self.reader.get_metrics_data()
        result: dict[frozenset[tuple[str, Any]], Any] = {}
        for resource in data.resource_metrics if data else []:
            for scope in resource.scope_metrics:
                for metric in scope.metrics:
                    if metric.name == name:
                        for point in metric.data.data_points:
                            key = frozenset(point.attributes.items())
                            result[key] = getattr(point, "value", point)
        return result


ATTRIBUTE_NAMES = {"operation": "garmin.operation", "outcome": "garmin.outcome", "method": "garmin.login.method"}


def attrs(**values: Any) -> frozenset[tuple[str, Any]]:
    return frozenset((ATTRIBUTE_NAMES[k], v) for k, v in values.items())
