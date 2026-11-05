from datetime import datetime
from zoneinfo import ZoneInfo

import pytest

from scripts.release_policy import in_window


def test_maintenance_window_uses_beijing_time_and_supports_overnight():
    at = lambda hour: datetime(2026, 11, 5, hour, tzinfo=ZoneInfo("Asia/Shanghai"))
    assert not in_window("02-05", at(1))
    assert in_window("02-05", at(2)) and in_window("02-05", at(4))
    assert not in_window("02-05", at(5))
    assert in_window("22-02", at(23)) and in_window("22-02", at(1))
    assert not in_window("22-02", at(2))
    assert in_window("always", at(12))
    with pytest.raises(ValueError):
        in_window("08-08")
    with pytest.raises(ValueError):
        in_window("invalid")
