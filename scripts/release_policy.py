"""部署时间窗口检查，只依赖宿主机 Python 标准库。"""

import re
import sys
from datetime import datetime, timedelta, timezone

BEIJING = timezone(timedelta(hours=8))


def in_window(spec, at=None):
    if spec == "always":
        return True
    if not re.fullmatch(r"\d{2}-\d{2}", spec):
        raise ValueError("更新窗口格式为 HH-HH，或显式设置 always。")
    start, end = map(int, spec.split("-"))
    if not 0 <= start < 24 or not 0 <= end <= 24 or start == end:
        raise ValueError("更新窗口小时无效。")
    hour = (at or datetime.now(BEIJING)).astimezone(BEIJING).hour
    return start <= hour < end if start < end else hour >= start or hour < end


if __name__ == "__main__":
    try:
        raise SystemExit(0 if in_window(sys.argv[1]) else 1)
    except (ValueError, IndexError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2) from None
