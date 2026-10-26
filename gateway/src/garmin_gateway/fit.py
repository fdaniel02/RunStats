import io
from typing import Any

import fitdecode


class InvalidFitFile(Exception):
    pass


# Sample field -> FIT record fields, preferred first. Older devices write speed and altitude instead of the enhanced
# (wider) fields.
FIELDS = {
    "latitudeSemicircles": ("position_lat",),
    "longitudeSemicircles": ("position_long",),
    "distanceMeters": ("distance",),
    "speedMps": ("enhanced_speed", "speed"),
    "altitudeMeters": ("enhanced_altitude", "altitude"),
    "heartRate": ("heart_rate",),
    "stepLengthMm": ("step_length",),
    "groundContactTimeMs": ("stance_time",),
    "verticalOscillationMm": ("vertical_oscillation",),
    "verticalRatioPercent": ("vertical_ratio",),
    "powerWatts": ("power",),
    "temperatureCelsius": ("temperature",),
}


def samples(data: bytes) -> list[dict[str, Any]]:
    """One sample per second of the activity's records, by offset from the first record."""
    if not data:
        raise InvalidFitFile("Not a valid FIT file: it is empty")
    by_second: dict[int, dict[str, Any]] = {}
    start = None
    try:
        with fitdecode.FitReader(io.BytesIO(data), check_crc=fitdecode.CrcCheck.RAISE) as reader:
            for frame in reader:
                if not isinstance(frame, fitdecode.FitDataMessage) or frame.name != "record":
                    continue
                timestamp = _value(frame, "timestamp")
                if timestamp is None:
                    continue
                start = start or timestamp
                offset = round((timestamp - start).total_seconds())
                # Devices can write more than one record in a second; the last one wins.
                if offset >= 0:
                    by_second[offset] = _sample(frame, offset)
    except fitdecode.FitError as e:
        raise InvalidFitFile(f"Not a valid FIT file: {e}") from e
    return [by_second[offset] for offset in sorted(by_second)]


def _sample(frame: fitdecode.FitDataMessage, offset: int) -> dict[str, Any]:
    sample: dict[str, Any] = {"offsetSeconds": offset}
    for name, fit_fields in FIELDS.items():
        sample[name] = next((v for f in fit_fields if (v := _value(frame, f)) is not None), None)
    sample["cadenceSpm"] = _steps_per_minute(frame)
    return sample


def _steps_per_minute(frame: fitdecode.FitDataMessage) -> float | None:
    # For running, FIT's cadence counts one foot (strides per minute) and keeps the fraction in a separate field.
    cadence = _value(frame, "cadence")
    if cadence is None:
        return None
    return (cadence + (_value(frame, "fractional_cadence") or 0)) * 2


def _value(frame: fitdecode.FitDataMessage, name: str) -> Any:
    return frame.get_value(name, fallback=None) if frame.has_field(name) else None
