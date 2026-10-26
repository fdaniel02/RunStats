import pytest
from fit_files import fit_file

from garmin_gateway.fit import InvalidFitFile, samples


def test_record_fields_become_a_sample_in_the_apps_units():
    data = fit_file(
        {
            "second": 0,
            "position_lat": 52.37,
            "position_long": -4.89,
            "distance": 12.34,
            "enhanced_speed": 2.95,
            # FIT stores altitude in steps of 0.2 m.
            "enhanced_altitude": -1.6,
            "heart_rate": 150,
            "cadence": 85,
            "fractional_cadence": 0.5,
            "step_length": 1150.0,
            "stance_time": 241.5,
            "vertical_oscillation": 89.2,
            "vertical_ratio": 6.95,
            "power": 310,
            "temperature": -3,
        }
    )

    [sample] = samples(data)

    assert sample == {
        "offsetSeconds": 0,
        "latitudeSemicircles": 624798437,
        "longitudeSemicircles": -58339972,
        "distanceMeters": 12.34,
        "speedMps": 2.95,
        "altitudeMeters": pytest.approx(-1.6),
        "heartRate": 150,
        "cadenceSpm": 171.0,
        "stepLengthMm": 1150.0,
        "groundContactTimeMs": 241.5,
        "verticalOscillationMm": 89.2,
        "verticalRatioPercent": 6.95,
        "powerWatts": 310,
        "temperatureCelsius": -3,
    }


def test_offsets_count_from_the_first_record():
    data = fit_file({"second": 0}, {"second": 1}, {"second": 5})

    assert [s["offsetSeconds"] for s in samples(data)] == [0, 1, 5]


def test_the_last_record_of_a_second_wins():
    data = fit_file(
        {"second": 0, "heart_rate": 140}, {"second": 0, "heart_rate": 141}, {"second": 1, "heart_rate": 142}
    )

    assert [(s["offsetSeconds"], s["heartRate"]) for s in samples(data)] == [(0, 141), (1, 142)]


def test_records_before_the_first_one_are_left_out():
    data = fit_file(
        {"second": 10, "heart_rate": 140}, {"second": 9, "heart_rate": 99}, {"second": 11, "heart_rate": 141}
    )

    assert [(s["offsetSeconds"], s["heartRate"]) for s in samples(data)] == [(0, 140), (1, 141)]


def test_records_without_a_timestamp_are_left_out():
    data = fit_file({"second": 0, "heart_rate": 140}, {"heart_rate": 99})

    assert [s["heartRate"] for s in samples(data)] == [140]


def test_missing_fields_are_none():
    [sample] = samples(fit_file({"second": 0, "heart_rate": 140, "distance": 3.5}))

    assert sample["heartRate"] == 140
    assert sample["latitudeSemicircles"] is None
    assert sample["cadenceSpm"] is None
    assert sample["powerWatts"] is None


def test_cadence_without_a_fraction():
    [sample] = samples(fit_file({"second": 0, "cadence": 80}))

    assert sample["cadenceSpm"] == 160


def test_older_devices_speed_and_altitude_are_used():
    [sample] = samples(fit_file({"second": 0, "speed": 3.1, "altitude": 12.0}))

    assert sample["speedMps"] == pytest.approx(3.1)
    assert sample["altitudeMeters"] == pytest.approx(12.0)


def test_file_without_records_has_no_samples():
    assert samples(fit_file()) == []


@pytest.mark.parametrize("data", [b"", b"not a fit file at all", fit_file({"second": 0})[:-1] + b"\x00"])
def test_invalid_or_damaged_files_are_rejected(data):
    with pytest.raises(InvalidFitFile, match="Not a valid FIT file"):
        samples(data)
