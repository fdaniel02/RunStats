import os
from collections.abc import Mapping

from opentelemetry import metrics, trace
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.metrics import MeterProvider
from opentelemetry.sdk.metrics import MeterProvider as SdkMeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider as SdkTracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.trace import TracerProvider

SCOPE = "garmin_gateway"

OPERATION = "garmin.operation"
OUTCOME = "garmin.outcome"
LOGIN_METHOD = "garmin.login.method"


def configure_telemetry(env: Mapping[str, str] = os.environ) -> bool:
    """Sends traces and metrics over OTLP/HTTP, only when there is a collector to send them to."""
    endpoint = env.get("OTEL_EXPORTER_OTLP_ENDPOINT", "").strip().rstrip("/")
    if not endpoint:
        return False
    resource = Resource.create({"service.name": env.get("OTEL_SERVICE_NAME") or "garmin-gateway"})
    tracing = SdkTracerProvider(resource=resource)
    tracing.add_span_processor(BatchSpanProcessor(OTLPSpanExporter(endpoint=f"{endpoint}/v1/traces")))
    trace.set_tracer_provider(tracing)
    reader = PeriodicExportingMetricReader(OTLPMetricExporter(endpoint=f"{endpoint}/v1/metrics"))
    metrics.set_meter_provider(SdkMeterProvider(resource=resource, metric_readers=[reader]))
    return True


class GarminTelemetry:
    """Spans and metrics for calls to Garmin. Holds only operation names, outcomes and timings, never Garmin data."""

    def __init__(self, tracer_provider: TracerProvider | None = None, meter_provider: MeterProvider | None = None):
        self.tracer = trace.get_tracer(SCOPE, tracer_provider=tracer_provider)
        meter = metrics.get_meter(SCOPE, meter_provider=meter_provider)
        self.calls = meter.create_counter("garmin.calls", unit="{call}", description="Calls to Garmin Connect")
        self.call_duration = meter.create_histogram(
            "garmin.call.duration", unit="s", description="Duration of calls to Garmin Connect, without throttling"
        )
        self.rate_limits = meter.create_counter(
            "garmin.rate_limits", unit="{event}", description="Times Garmin Connect answered with a rate limit"
        )
        self.logins = meter.create_counter("garmin.logins", unit="{login}", description="Logins to Garmin Connect")
