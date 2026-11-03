import base64
import binascii
from datetime import UTC, datetime
from io import BytesIO
from itertools import combinations, pairwise
from typing import Literal

from PIL import Image
from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, field_serializer, field_validator, model_validator


class Input(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=False, allow_inf_nan=False)


class Credentials(Input):
    login: str = Field(min_length=3, max_length=80, pattern=r"^[a-zA-Z0-9_@.\-]+$")
    password: str = Field(min_length=10, max_length=128)
    name: str = Field(default="老师", min_length=1, max_length=40)


class ClassInput(Input):
    name: str = Field(min_length=1, max_length=60)
    stage: Literal["小学", "初中", "高中"] = "小学"
    subjects: list[str] = Field(
        default=["语文", "数学", "英语", "科学", "体育", "美术", "班会"], min_length=1, max_length=20
    )

    @field_validator("name", "subjects")
    @classmethod
    def meaningful(cls, value):
        if isinstance(value, str):
            if not value.strip():
                raise ValueError("名称不能全为空白")
            return value.strip()
        if len(set(value)) != len(value) or any(not s.strip() or len(s) > 20 for s in value):
            raise ValueError("科目不能为空、重复或超过 20 字")
        return value


class DraftInput(Input):
    id: str = Field(min_length=1, max_length=36)
    kind: Literal["homework", "todo"]
    classIds: list[str] = Field(min_length=1, max_length=10)
    subject: str = Field(min_length=1, max_length=20)
    body: str = Field(max_length=500)
    from_: AwareDatetime = Field(alias="from")
    until: AwareDatetime
    due: AwareDatetime | Literal[""] = ""
    revision: int = Field(ge=0)
    updated: str = ""
    sourceId: str | None = Field(default=None, max_length=36)
    baseRevision: int | None = Field(default=None, ge=1)
    reason: str = Field(default="", max_length=100)
    priority: bool = False

    @field_serializer("from_", "until", "due")
    def utc_time(self, value):
        # 存储使用 UTC，界面再按班级时区显示，避免字符串排序受不同偏移影响。
        return value.astimezone(UTC).isoformat() if isinstance(value, datetime) else ""

    @model_validator(mode="after")
    def validate_targets(self):
        if len(set(self.classIds)) != len(self.classIds):
            raise ValueError("目标班级不能重复")
        return self


class PublishInput(Input):
    draftId: str = Field(min_length=1, max_length=36)
    draftRevision: int = Field(ge=1)


class RevisionInput(Input):
    revision: int = Field(ge=1)
    reason: str = Field(default="", max_length=100)
    until: AwareDatetime | None = None


class GridRect(Input):
    x: int = Field(ge=0, le=11)
    y: int = Field(ge=0, le=11)
    w: int = Field(ge=5, le=12)
    h: int = Field(ge=4, le=12)

    @model_validator(mode="after")
    def within_canvas(self):
        if self.x + self.w > 12 or self.y + self.h > 12:
            raise ValueError("模块不能超出 12 × 12 网格")
        return self


def grid_default():
    return {
        "homework": {"x": 0, "y": 0, "w": 7, "h": 12},
        "todo": {"x": 7, "y": 0, "w": 5, "h": 5},
        "timetable": {"x": 7, "y": 5, "w": 5, "h": 7},
    }


class LayoutInput(Input):
    theme: Literal["mica_light", "mica_dark"] = "mica_light"
    material: Literal["subtle", "solid"] = "subtle"
    backgroundMode: Literal["mica", "color", "image"] = "mica"
    backgroundColor: str = Field(default="#E7E8EB", pattern=r"^#[0-9a-fA-F]{6}$")
    backgroundImage: str = Field(default="", max_length=524311)
    surfaceOpacity: int = Field(default=38, ge=20, le=95)
    font: Literal["small", "medium", "large"] = "medium"
    iconMargin: Literal["left", "right", "none"] = "left"
    interval: Literal[15, 20, 30] = 20
    modules: dict[Literal["datetime", "homework", "todo", "timetable", "countdown", "message"], bool]
    grid: dict[Literal["homework", "todo", "timetable"], GridRect] = Field(
        default_factory=grid_default, validate_default=True
    )
    reverse: bool = False
    revision: int = Field(ge=1)

    @field_validator("backgroundImage")
    @classmethod
    def raster_background(cls, value):
        if not value:
            return value
        prefix = "data:image/jpeg;base64,"
        if not value.startswith(prefix):
            raise ValueError("背景须为上传后生成的 JPEG 图片")
        try:
            raw = base64.b64decode(value[len(prefix) :], validate=True)
            with Image.open(BytesIO(raw)) as image:
                if image.format != "JPEG" or image.width > 1920 or image.height > 1080:
                    raise ValueError("背景图片不能超过 1920×1080")
                image.load()
        except (binascii.Error, OSError) as exc:
            raise ValueError("背景图片无法读取，请重新上传") from exc
        return value

    @field_validator("modules")
    @classmethod
    def complete_modules(cls, value):
        value.setdefault("datetime", True)
        if set(value) != {"datetime", "homework", "todo", "timetable", "countdown", "message"}:
            raise ValueError("必须提交完整模块配置")
        return value

    @model_validator(mode="after")
    def valid_grid(self):
        if self.backgroundMode == "image" and not self.backgroundImage:
            raise ValueError("请先上传背景图片")
        if set(self.grid) != {"homework", "todo", "timetable"}:
            raise ValueError("必须提交完整网格配置")
        rects = [(key, GridRect.model_validate(value)) for key, value in self.grid.items() if self.modules[key]]
        for (a_key, a), (b_key, b) in combinations(rects, 2):
            if a.x < b.x + b.w and b.x < a.x + a.w and a.y < b.y + b.h and b.y < a.y + a.h:
                labels = {"homework": "作业", "todo": "提醒", "timetable": "课表"}
                raise ValueError(f"{labels[a_key]}与{labels[b_key]}模块重叠")
        if self.modules["timetable"] and GridRect.model_validate(self.grid["timetable"]).h < 7:
            raise ValueError("课表高度至少 7 格，确保最多 12 节课程可读")
        return self


class Period(Input):
    start: str = Field(pattern=r"^(?:[01]\d|2[0-3]):[0-5]\d$")
    end: str = Field(pattern=r"^(?:[01]\d|2[0-3]):[0-5]\d$")
    subject: str = Field(min_length=1, max_length=20)


class TimetableInput(Input):
    revision: int = Field(ge=1)
    effectiveFrom: str = Field(pattern=r"^\d{4}-\d{2}-\d{2}$")
    effectiveUntil: str = Field(pattern=r"^\d{4}-\d{2}-\d{2}$")
    weekdays: dict[str, list[Period]]

    @model_validator(mode="after")
    def no_overlaps(self):
        a, b = datetime.fromisoformat(self.effectiveFrom), datetime.fromisoformat(self.effectiveUntil)
        if a > b:
            raise ValueError("学期结束不能早于开始")
        if any(k not in [str(n) for n in range(1, 8)] for k in self.weekdays):
            raise ValueError("星期范围为 1 至 7")
        for key, periods in self.weekdays.items():
            if len(periods) > 12:
                raise ValueError("一天最多 12 节")
            ordered = sorted(periods, key=lambda p: p.start)
            if any(p.start >= p.end for p in ordered) or any(a.end > b.start for a, b in pairwise(ordered)):
                raise ValueError("节次时间无效或重叠")
            self.weekdays[key] = ordered
        return self


class OverrideInput(Input):
    revision: int = Field(ge=1)
    date: str = Field(pattern=r"^\d{4}-\d{2}-\d{2}$")
    mode: Literal["closed", "weekday", "custom"]
    weekday: int = Field(default=1, ge=1, le=7)
    periods: list[Period] = Field(default=[], max_length=12)


class DisplayInput(Input):
    revision: int = Field(ge=1)
    message: str = Field(default="", max_length=80)
    event: str = Field(default="", max_length=40)
    eventDate: str = ""


class PairInput(Input):
    name: str = Field(min_length=1, max_length=60)


class PairConfirm(Input):
    code: str = Field(pattern=r"^\d{6}$")
    classId: str = Field(min_length=1, max_length=36)


class ControlInput(Input):
    action: Literal["sync", "pause", "resume", "set_page", "auto_page"]
    expectedControlRevision: int = Field(ge=0)
    pageSetId: str = Field(default="", max_length=100)
    page: int = Field(default=1, ge=1, le=10000)


class ReportInput(Input):
    clientVersion: str = Field(default="0.1.0", max_length=30)
    bindingId: str = Field(max_length=36)
    syncEpoch: str = Field(max_length=36)
    stateReportSeq: int = Field(ge=1)
    localControlEpoch: int = Field(ge=0)
    localPaused: bool
    remotePaused: bool
    cachedVersion: int = Field(ge=0)
    appliedVersion: int = Field(ge=0)
    applyState: Literal["applied", "paused", "deferred", "error", "waiting"]
    pageSetId: str = Field(max_length=100)
    page: int = Field(ge=1, le=10000)
    pageCount: int = Field(ge=1, le=10000)
    manualUntil: float = Field(default=0, ge=0)
    effectiveTheme: Literal["mica_light", "mica_dark", "system_high_contrast"]
    effectiveMaterial: Literal["subtle", "solid"]
    materialReason: str = Field(default="", max_length=200)
    renderedAt: float = Field(default=0, ge=0)
    error: str = Field(default="", max_length=200)


class CommandAck(ReportInput):
    commandId: str = Field(max_length=36)
    result: Literal["executed", "deferred", "rejected"]
    startedAt: float | None = Field(default=None, ge=0)
    finishedAt: float | None = Field(default=None, ge=0)


class InviteInput(Input):
    subjects: list[str] = Field(min_length=1, max_length=20)


class MemberInput(Input):
    role: Literal["admin", "teacher"]
    subjects: list[str] = Field(max_length=20)
    active: bool = True


class TransferInput(Input):
    recipient: str = Field(min_length=1, max_length=36)
