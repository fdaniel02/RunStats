from typing import Any

from fit_tool.fit_file_builder import FitFileBuilder
from fit_tool.profile.messages.event_message import EventMessage
from fit_tool.profile.messages.file_id_message import FileIdMessage
from fit_tool.profile.messages.record_message import RecordMessage
from fit_tool.profile.profile_type import FileType, Manufacturer

START_MS = 1_761_385_800_000  # 2025-10-25 09:50:00 UTC


def fit_file(*records: dict[str, Any]) -> bytes:
    """A synthetic activity FIT file. Each record gives `second` (from the start) and fit-tool's field values:
    positions in degrees, the other fields in FIT's units."""
    builder = FitFileBuilder(auto_define=True)
    file_id = FileIdMessage()
    file_id.type = FileType.ACTIVITY
    file_id.manufacturer = Manufacturer.GARMIN.value
    file_id.time_created = START_MS
    builder.add(file_id)
    builder.add(EventMessage())
    for values in records:
        record = RecordMessage()
        values = dict(values)
        second = values.pop("second", None)
        if second is not None:
            record.timestamp = START_MS + second * 1000
        for name, value in values.items():
            setattr(record, name, value)
        builder.add(record)
    return builder.build().to_bytes()
