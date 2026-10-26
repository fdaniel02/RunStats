import io

import pytest
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import ConsoleMetricExporter
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

from garmin_gateway import telemetry


@pytest.fixture
def installed(monkeypatch):
    """Captures what configure_telemetry installs, instead of changing the process-wide providers."""
    providers = {}
    endpoints = []
    monkeypatch.setattr(telemetry.trace, "set_tracer_provider", lambda p: providers.update(tracing=p))
    monkeypatch.setattr(telemetry.metrics, "set_meter_provider", lambda p: providers.update(metrics=p))

    def span_exporter(endpoint):
        endpoints.append(endpoint)
        return InMemorySpanExporter()

    def metric_exporter(endpoint):
        endpoints.append(endpoint)
        return ConsoleMetricExporter(out=io.StringIO())

    monkeypatch.setattr(telemetry, "OTLPSpanExporter", span_exporter)
    monkeypatch.setattr(telemetry, "OTLPMetricExporter", metric_exporter)
    yield providers, endpoints
    for provider in providers.values():
        provider.shutdown()


@pytest.mark.parametrize("endpoint", [None, "", "  "])
def test_nothing_is_set_up_without_a_collector_endpoint(installed, endpoint):
    providers, _ = installed
    env = {} if endpoint is None else {"OTEL_EXPORTER_OTLP_ENDPOINT": endpoint}

    assert telemetry.configure_telemetry(env) is False
    assert providers == {}


def test_traces_and_metrics_go_to_the_collector(installed):
    providers, endpoints = installed

    assert telemetry.configure_telemetry({"OTEL_EXPORTER_OTLP_ENDPOINT": "http://alloy:4318/"}) is True

    assert isinstance(providers["tracing"], TracerProvider)
    assert isinstance(providers["metrics"], MeterProvider)
    assert endpoints == ["http://alloy:4318/v1/traces", "http://alloy:4318/v1/metrics"]
    assert providers["tracing"].resource.attributes["service.name"] == "garmin-gateway"


def test_service_name_comes_from_OTEL_SERVICE_NAME(installed):
    providers, _ = installed

    telemetry.configure_telemetry({"OTEL_EXPORTER_OTLP_ENDPOINT": "http://alloy:4318", "OTEL_SERVICE_NAME": "gw-test"})

    assert providers["tracing"].resource.attributes["service.name"] == "gw-test"
