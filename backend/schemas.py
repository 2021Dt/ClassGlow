import base64
import binascii
from datetime import UTC, date, datetime
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

    @model_validator(mode="before")
    @classmethod
    def stage_subjects(cls, value):
        # 未指定时按学段补全；已有班级和显式填写的自定义学科保持原样。
        if isinstance(value, dict) and "subjects" not in value:
            presets = {
                "小学": [
                    "语文",
                    "数学",
                    "英语",
                    "科学",
                    "道德与法治",
                    "体育",
                    "音乐",
                    "美术",
                    "信息科技",
                    "劳动",
                    "班会",
                ],
                "初中": [
                    "语文",
                    "数学",
                    "英语",
                    "物理",
                    "化学",
                    "生物",
                    "历史",
                    "地理",
                    "道德与法治",
                    "体育",
                    "音乐",
                    "美术",
                    "信息科技",
                    "劳动",
                    "班会",
                ],
                "高中": [
                    "语文",
                    "数学",
                    "英语",
                    "物理",
                    "化学",
                    "生物",
                    "政治",
                    "历史",
                    "地理",
                    "体育",
                    "音乐",
                    "美术",
                    "信息技术",
                    "通用技术",
                    "班会",
                ],
            }
            value = {**value, "subjects": presets.get(value.get("stage", "小学"), presets["小学"])}
        return value

    @field_validator("name", "subjects")
    @classmethod
    def meaningful(cls, value):
        if isinstance(value, str):
            if not value.strip():
                raise ValueError("名称不能全为空白")
            return value.strip()
        value = [s.strip() for s in value]
        if len(set(value)) != len(value) or any(not s or len(s) > 20 for s in value):
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


class ShapePoint(Input):
    x: int = Field(ge=0, le=100)
    y: int = Field(ge=0, le=100)


class CanvasWidget(Input):
    id: str = Field(pattern=r"^[a-zA-Z0-9_-]{1,80}$")
    kind: Literal["homework", "todo", "timetable", "datetime", "countdown", "message", "text", "image", "shape"]
    groupId: str | None = Field(default=None, pattern=r"^[a-zA-Z0-9_-]{1,80}$")
    dynamicText: bool = False
    subjectFilter: str = Field(default="", max_length=20)
    dataSource: Literal["classroom", "custom"] = "classroom"
    event: str = Field(default="", max_length=60)
    eventDate: str = Field(default="", pattern=r"^(\d{4}-\d{2}-\d{2})?$")
    showTitle: bool = True
    shape: Literal["rectangle", "ellipse", "pill", "triangle", "diamond", "hexagon", "star", "custom"] = "rectangle"
    vertices: list[ShapePoint] = Field(default_factory=list, max_length=24)
    x: int = Field(ge=0, le=1552)
    y: int = Field(ge=0, le=852)
    w: int = Field(ge=48, le=1600)
    h: int = Field(ge=48, le=900)
    title: str = Field(default="", max_length=30)
    text: str = Field(default="", max_length=2000)
    image: str = Field(default="", max_length=524311)
    visible: bool = True
    locked: bool = False
    fontSize: int = Field(default=32, ge=16, le=96)
    fontFamily: Literal["sans", "serif", "kai"] = "sans"
    fontWeight: Literal[400, 500, 600, 700] = 400
    lineHeight: float = Field(default=1.5, ge=1.2, le=2)
    titleSize: int = Field(default=28, ge=16, le=64)
    backgroundImage: str = Field(default="", max_length=524311)
    backgroundFit: Literal["cover", "contain"] = "cover"
    backgroundPosition: Literal["center", "top", "bottom", "left", "right"] = "center"
    textColor: str = Field(default="", pattern=r"^(#[0-9a-fA-F]{6})?$")
    fillColor: str = Field(default="#FFFFFF", pattern=r"^#[0-9a-fA-F]{6}$")
    surface: Literal["glass", "solid", "none"] = "glass"
    opacity: int = Field(default=45, ge=0, le=100)
    radius: int = Field(default=24, ge=0, le=80)
    align: Literal["left", "center", "right"] = "left"

    @field_validator("image", "backgroundImage")
    @classmethod
    def valid_image(cls, value):
        return LayoutInput.raster_background(value)

    @model_validator(mode="after")
    def usable_size(self):
        if self.eventDate:
            date.fromisoformat(self.eventDate)
        if self.kind == "shape" and self.shape == "custom" and len({(point.x, point.y) for point in self.vertices}) < 3:
            raise ValueError("自由轮廓至少需要三个不同的顶点")
        if self.x + self.w > 1600 or self.y + self.h > 900:
            raise ValueError("组件不能超出画布")
        # 数据组件保留至少一行正文空间；超出的内容分页，不缩字或悄悄截断。
        if self.kind in ("homework", "todo", "timetable", "datetime", "countdown", "message"):
            minimum_height = 100 if not self.showTitle and self.kind not in ("datetime", "countdown") else 180
            if self.w < 280 or self.h < minimum_height:
                raise ValueError(f"当前数据组件至少为 280 × {minimum_height}，以保证文字可读")
        if self.kind == "text" and (self.w < 120 or self.h < 100):
            raise ValueError("文字组件至少为 120 × 100")
        if self.kind == "text" and self.w < self.fontSize + 32:
            raise ValueError("文字组件宽度不足，请加宽组件或调小字号")
        if self.kind == "text" and self.h < self.fontSize * self.lineHeight + 32:
            raise ValueError("文字组件高度不足以显示一行当前字号的文字")
        title_inset = 46 + self.titleSize * 1.5 if self.showTitle else 32
        if (
            self.kind in ("homework", "todo", "timetable", "message")
            and self.h < title_inset + self.fontSize * self.lineHeight
        ):
            raise ValueError("卡片高度不足以显示标题和一行正文，请增高卡片或调小字号")
        if self.kind in ("datetime", "countdown") and self.h < 105 + (self.fontSize + 24) * 1.2:
            raise ValueError("时钟或倒计时高度不足，请增高组件或调小字号")
        if self.kind == "image" and self.visible and not self.image:
            raise ValueError("请为图片组件选择图片，或先隐藏它")
        return self


class LayoutInput(Input):
    editor: Literal["grid", "canvas"] = "grid"
    canvas: list[CanvasWidget] = Field(default_factory=list, max_length=24)
    theme: Literal["mica_light", "mica_dark"] = "mica_light"
    material: Literal["subtle", "solid"] = "subtle"
    backgroundMode: Literal["mica", "color", "image"] = "mica"
    backgroundColor: str = Field(default="#E7E8EB", pattern=r"^#[0-9a-fA-F]{6}$")
    backgroundImage: str = Field(default="", max_length=524311)
    surfaceOpacity: int = Field(default=38, ge=0, le=95)
    panelImage: str = Field(default="", max_length=524311)
    panelImageFit: Literal["cover", "contain"] = "cover"
    panelImagePosition: Literal["center", "top", "bottom", "left", "right"] = "center"
    font: Literal["small", "medium", "large"] = "medium"
    iconMargin: Literal["left", "right", "none"] = "left"
    interval: Literal[15, 20, 30] = 20
    modules: dict[Literal["datetime", "homework", "todo", "timetable", "countdown", "message"], bool]
    grid: dict[Literal["homework", "todo", "timetable"], GridRect] = Field(
        default_factory=grid_default, validate_default=True
    )
    reverse: bool = False
    revision: int = Field(ge=1)

    @field_validator("backgroundImage", "panelImage")
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
        if self.editor == "canvas":
            if len({item.id for item in self.canvas}) != len(self.canvas):
                raise ValueError("组件标识不能重复")
            return self
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
    name: str | None = Field(default=None, min_length=1, max_length=60)

    @field_validator("name")
    @classmethod
    def clean_name(cls, value):
        return DeviceName(name=value).name if value is not None else None


class DeviceName(Input):
    name: str = Field(min_length=1, max_length=60)

    @field_validator("name")
    @classmethod
    def clean_name(cls, value):
        value = value.strip()
        if not value or any(ord(char) < 32 for char in value):
            raise ValueError("请输入可识别的电脑名称")
        return value


class ScreenNoticeStyle(Input):
    fontFamily: Literal["sans", "serif", "kai"] = "sans"
    fontSize: int = Field(default=56, ge=24, le=120)
    bold: bool = True
    textColor: str = Field(default="#ffffff", pattern=r"^#[0-9a-fA-F]{6}$")
    backgroundColor: str = Field(default="#17458c", pattern=r"^#[0-9a-fA-F]{6}$")
    opacity: int = Field(default=95, ge=20, le=100)
    position: Literal["top", "center", "bottom"] = "top"
    surface: Literal["fullscreen", "card"] = "fullscreen"
    width: int = Field(default=72, ge=40, le=90)
    radius: int = Field(default=32, ge=0, le=64)
    flowSeconds: int = Field(default=14, ge=6, le=40)
    direction: Literal["left", "right"] = "left"


class NoticeElement(Input):
    id: str = Field(min_length=1, max_length=40)
    kind: Literal["text", "card", "image", "countdown"]
    x: int = Field(ge=0, le=1552)
    y: int = Field(ge=0, le=852)
    w: int = Field(ge=48, le=1600)
    h: int = Field(ge=48, le=900)
    text: str = Field(default="", max_length=240)
    fontFamily: Literal["sans", "serif", "kai"] = "sans"
    fontSize: int = Field(default=56, ge=24, le=160)
    bold: bool = True
    align: Literal["left", "center", "right"] = "center"
    textColor: str = Field(default="#ffffff", pattern=r"^#[0-9a-fA-F]{6}$")
    fillColor: str = Field(default="#215544", pattern=r"^#[0-9a-fA-F]{6}$")
    opacity: int = Field(default=95, ge=0, le=100)
    radius: int = Field(default=32, ge=0, le=120)
    image: str = Field(default="", max_length=240000)
    imageFit: Literal["cover", "contain"] = "contain"
    hidden: bool = False
    locked: bool = False

    @field_validator("image")
    @classmethod
    def raster_image(cls, value):
        return LayoutInput.raster_background(value)

    @model_validator(mode="after")
    def valid_element(self):
        if self.x + self.w > 1600 or self.y + self.h > 900:
            raise ValueError("组件不能超出 1600×900 画布")
        if (self.kind == "image") != bool(self.image):
            raise ValueError("图片组件须上传图片，其他组件不能附带图片")
        return self


class NoticeCanvas(Input):
    backgroundColor: str = Field(default="#102a43", pattern=r"^#[0-9a-fA-F]{6}$")
    backgroundOpacity: int = Field(default=35, ge=0, le=100)
    elements: list[NoticeElement] = Field(min_length=1, max_length=12)

    @model_validator(mode="after")
    def valid_canvas(self):
        if len({e.id for e in self.elements}) != len(self.elements):
            raise ValueError("组件标识不能重复")
        if not any(
            not e.hidden and (e.kind in ("image", "countdown") or e.text.strip() or e.kind == "card" and e.opacity > 0)
            for e in self.elements
        ):
            raise ValueError("至少保留一个有内容的可见组件")
        if len(self.model_dump_json().encode()) > 524288:
            raise ValueError("画布超过 512 KB，请删除或更换较小的图片")
        return self


class ScreenNotice(Input):
    mode: Literal["marquee", "barrage", "popup", "countdown", "canvas"]
    message: str = Field(min_length=1, max_length=160)
    duration: int = Field(default=30, ge=5, le=3600)
    tone: Literal["blue", "amber", "red"] = "blue"
    overFullscreen: bool = False
    style: ScreenNoticeStyle | None = None
    canvas: NoticeCanvas | None = None

    @field_validator("message")
    @classmethod
    def meaningful_message(cls, value):
        if not value.strip():
            raise ValueError("请填写提醒内容")
        return value.strip()

    @model_validator(mode="after")
    def barrage_lines(self):
        if (self.mode == "canvas") != (self.canvas is not None):
            raise ValueError("自由画布提醒须附带画布，其他提醒不能附带画布")
        if self.mode == "canvas" and self.style is not None:
            raise ValueError("自由画布通过组件设置样式")
        if (
            self.style is not None
            and self.mode == "barrage"
            and len([line for line in self.message.split("\n") if line]) > 3
        ):
            raise ValueError("弹幕最多分为 3 条，请合并多余的行")
        return self


class ControlInput(Input):
    action: Literal["sync", "pause", "resume", "set_page", "auto_page", "notice", "stop_notice"]
    expectedControlRevision: int = Field(ge=0)
    pageSetId: str = Field(default="", max_length=100)
    page: int = Field(default=1, ge=1, le=10000)
    notice: ScreenNotice | None = None

    @model_validator(mode="after")
    def notice_required(self):
        if (self.action == "notice") != (self.notice is not None):
            raise ValueError("发布醒目提醒时须填写提醒内容")
        return self


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
    activeNoticeId: str = Field(default="", max_length=36)
    noticeEndsAt: float = Field(default=0, ge=0)


class DeviceDisplayInput(ReportInput):
    requestId: str = Field(min_length=1, max_length=36)
    availability: Literal["displayed", "paused", "disabled", "replaced", "unavailable"]
    imageBase64: str = Field(default="", max_length=550000)


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
