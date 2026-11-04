export type Role = "owner" | "admin" | "teacher";
export type Kind = "homework" | "todo";
export type ModuleKey =
  "datetime" | "homework" | "todo" | "timetable" | "countdown" | "message";
export type GridKey = "homework" | "todo" | "timetable";
export interface GridRect {
  x: number;
  y: number;
  w: number;
  h: number;
}
export type Theme = "mica_light" | "mica_dark";
export type BoardFont = "sans" | "serif" | "kai";
export type ImageFit = "cover" | "contain";
export type ImagePosition = "center" | "top" | "bottom" | "left" | "right";
export interface Layout {
  editor?: "grid" | "canvas";
  canvas?: CanvasWidget[];
  theme: Theme;
  material: "subtle" | "solid";
  backgroundMode?: "mica" | "color" | "image";
  backgroundColor?: string;
  backgroundImage?: string;
  surfaceOpacity?: number;
  panelImage?: string;
  panelImageFit?: ImageFit;
  panelImagePosition?: ImagePosition;
  font: "small" | "medium" | "large";
  iconMargin: "left" | "right" | "none";
  interval: 15 | 20 | 30;
  modules: Record<ModuleKey, boolean>;
  reverse: boolean;
  revision: number;
  grid: Record<GridKey, GridRect>;
}
export type WidgetKind = ModuleKey | "text" | "image" | "shape";
export type ShapeKind =
  | "rectangle"
  | "ellipse"
  | "pill"
  | "triangle"
  | "diamond"
  | "hexagon"
  | "star"
  | "custom";
export interface ShapePoint {
  x: number;
  y: number;
}
export interface CanvasWidget {
  id: string;
  kind: WidgetKind;
  groupId?: string | null;
  dynamicText?: boolean;
  subjectFilter?: string;
  showTitle?: boolean;
  shape?: ShapeKind;
  vertices?: ShapePoint[];
  x: number;
  y: number;
  w: number;
  h: number;
  title: string;
  text: string;
  image: string;
  visible: boolean;
  locked: boolean;
  fontSize: number;
  fontFamily?: BoardFont;
  fontWeight?: 400 | 500 | 600 | 700;
  lineHeight?: number;
  titleSize?: number;
  backgroundImage?: string;
  backgroundFit?: ImageFit;
  backgroundPosition?: ImagePosition;
  textColor: string;
  fillColor: string;
  surface: "glass" | "solid" | "none";
  opacity: number;
  radius: number;
  align: "left" | "center" | "right";
}
export interface Classroom {
  premiumActive?: boolean;
  premiumUntil?: number;
  licenseOwner?: string;
  authorizedSubjects?: string[];
  id: string;
  name: string;
  stage: string;
  role: Role;
  subjects: string[];
  version: number;
  layout: Layout;
  message: string;
  event: string;
  eventDate: string;
  timetable: string[];
  archived?: boolean;
  revision?: number;
  currentPeriod?: number;
  nextPeriod?: string;
  timetableState?: string;
  templates?: {
    effectiveFrom: string;
    effectiveUntil: string;
    weekdays: Record<string, { start: string; end: string; subject: string }[]>;
  }[];
  overrides?: Record<
    string,
    {
      mode: string;
      weekday: number;
      periods: { start: string; end: string; subject: string }[];
    }
  >;
}
export interface Draft {
  id: string;
  kind: Kind;
  classIds: string[];
  subject: string;
  body: string;
  from: string;
  until: string;
  due: string;
  revision: number;
  updated: string;
  sourceId?: string;
  baseRevision?: number;
  reason: string;
  priority: boolean;
}

export function restoreLocalDraft(value: unknown): Draft | null {
  // 本机存储也属于不可信输入。只验证可安全编辑的结构；未完成或超长正文仍须保留，不能静默截断。
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const d = value as Record<string, unknown>;
  const instant = (v: unknown): v is string =>
    typeof v === "string" && Number.isFinite(Date.parse(v));
  const revision = (v: unknown): v is number =>
    typeof v === "number" && Number.isSafeInteger(v) && v >= 0;
  if (
    typeof d.id !== "string" ||
    !d.id ||
    (d.kind !== "homework" && d.kind !== "todo") ||
    !Array.isArray(d.classIds) ||
    !d.classIds.every((id) => typeof id === "string" && id.length > 0) ||
    typeof d.subject !== "string" ||
    typeof d.body !== "string" ||
    !instant(d.from) ||
    !instant(d.until) ||
    !instant(d.updated) ||
    (d.due !== "" && !instant(d.due)) ||
    !revision(d.revision) ||
    (d.sourceId !== undefined && typeof d.sourceId !== "string") ||
    (d.baseRevision !== undefined && !revision(d.baseRevision)) ||
    (d.reason !== undefined && typeof d.reason !== "string") ||
    (d.priority !== undefined && typeof d.priority !== "boolean")
  )
    return null;
  // 兼容较早版本的本机草稿；损坏记录由调用方忽略并保留原始存储，便于人工找回。
  return {
    id: d.id,
    kind: d.kind,
    classIds: [...d.classIds],
    subject: d.subject,
    body: d.body,
    from: d.from,
    until: d.until,
    due: d.due,
    revision: d.revision,
    updated: d.updated,
    sourceId: d.sourceId,
    baseRevision: d.baseRevision,
    reason: d.reason ?? "",
    priority: d.priority ?? false,
  };
}
export interface Content {
  id: string;
  classId: string;
  kind: Kind;
  subject: string;
  body: string;
  author: string;
  from: string;
  until: string;
  due: string;
  revision: number;
  created: string;
  state: "published" | "completed" | "withdrawn";
  batchId: string;
  reason?: string;
  authorId?: string;
  priority?: boolean;
}
export interface Device {
  clientVersion?: string;
  id: string;
  classId: string;
  name: string;
  online: boolean;
  localPaused: boolean;
  remotePaused: boolean;
  cached: number;
  applied: number;
  page: number;
  pageCount: number;
  manualUntil: number;
  effectiveTheme: Theme | "system_high_contrast";
  effectiveMaterial: "subtle" | "solid";
  reduceEffects?: boolean;
  deferred: boolean;
  reported: string;
  revision: number;
  pageSetId?: string;
  materialReason?: string;
  error?: string;
  applyState?: string;
}
export interface Command {
  actor?: string;
  id: string;
  deviceId: string;
  action: "sync" | "pause" | "resume" | "set_page" | "auto_page";
  state:
    | "accepted"
    | "executed"
    | "deferred"
    | "rejected"
    | "expired"
    | "superseded";
  time: string;
  detail: string;
  targetPage?: number;
}
export interface Model {
  schema: 1;
  demo: boolean;
  classes: Classroom[];
  selected: string;
  drafts: Draft[];
  contents: Content[];
  devices: Device[];
  commands: Command[];
}

export const uid = () => crypto.randomUUID();
export const isAdmin = (c?: Classroom) =>
  !!c && (c.role === "owner" || c.role === "admin");
export const roleLabel = (r: Role) =>
  ({ owner: "班主任", admin: "班主任", teacher: "普通老师" })[r];
export function defaultLayout(): Layout {
  return {
    theme: "mica_light",
    material: "subtle",
    backgroundMode: "mica",
    backgroundColor: "#E7E8EB",
    backgroundImage: "",
    surfaceOpacity: 38,
    font: "medium",
    iconMargin: "left",
    interval: 20,
    modules: {
      datetime: true,
      homework: true,
      todo: true,
      timetable: true,
      countdown: true,
      message: true,
    },
    reverse: false,
    revision: 1,
    grid: {
      homework: { x: 0, y: 0, w: 7, h: 12 },
      todo: { x: 7, y: 0, w: 5, h: 5 },
      timetable: { x: 7, y: 5, w: 5, h: 7 },
    },
  };
}
export const emptyModel = (): Model => ({
  schema: 1,
  demo: false,
  classes: [],
  selected: "",
  drafts: [],
  contents: [],
  devices: [],
  commands: [],
});
export function dateKey(value: Date | string | number = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Shanghai",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(value));
}
export function addDays(key: string, days: number): string {
  return new Date(new Date(`${key}T12:00:00+08:00`).getTime() + days * 86400000)
    .toISOString()
    .slice(0, 10);
}
export function localInput(value: Date | string = new Date()): string {
  const d = new Date(value);
  return `${dateKey(d)}T${new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Shanghai", hour: "2-digit", minute: "2-digit", hour12: false }).format(d)}`;
}
export function toInstant(value: string): string {
  return new Date(`${value}:00+08:00`).toISOString();
}
export function defaultUntil(from: string, kind: Kind): string {
  return `${addDays(dateKey(from), 1)}T${kind === "homework" ? "08" : "18"}:00:00+08:00`;
}
export function timeLabel(value: string | number, full = false): string {
  return new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai",
    ...(full ? { month: "numeric", day: "numeric" } : {}),
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(new Date(value));
}
export function visible(c: Content, now: number): boolean {
  return (
    c.state === "published" &&
    new Date(c.from).getTime() <= now &&
    now < new Date(c.until).getTime()
  );
}
export function statusLabel(c: Content, now: number): string {
  if (c.state === "withdrawn") return "已撤回";
  if (c.state === "completed") return "已完成";
  if (now < new Date(c.from).getTime()) return "待展示";
  if (now >= new Date(c.until).getTime()) return "展示结束";
  return "展示中";
}
export function newDraft(classId: string, kind: Kind, subject = "语文"): Draft {
  const from = new Date().toISOString();
  return {
    id: uid(),
    kind,
    classIds: [classId],
    subject: kind === "todo" ? "公共提醒" : subject,
    body: "",
    from,
    until: defaultUntil(from, kind),
    due: "",
    revision: 0,
    updated: from,
    reason: "",
    priority: false,
  };
}
export function canPublish(c: Classroom, subject: string): boolean {
  if (c.archived) return false;
  return subject === "公共提醒"
    ? isAdmin(c)
    : c.subjects.includes(subject) &&
        (isAdmin(c) || (c.authorizedSubjects ?? c.subjects).includes(subject));
}

export function layoutIssue(layout: Layout): string {
  if (layout.backgroundMode === "image" && !layout.backgroundImage)
    return "请先上传背景图片。";
  if (layout.editor === "canvas") {
    if (!layout.canvas || layout.canvas.length > 24)
      return "画布最多放置 24 个组件。";
    if (
      new TextEncoder().encode(JSON.stringify(layout)).length >
      2 * 1024 * 1024 - 1024
    )
      return "图片总量过大，请减少图片或使用更简洁的图片。";
    for (const item of layout.canvas) {
      if (
        item.x < 0 ||
        item.y < 0 ||
        item.w < 48 ||
        item.h < 48 ||
        item.x + item.w > 1600 ||
        item.y + item.h > 900
      )
        return "组件超出画布边界，请调整后发布。";
      if (item.kind === "image" && !item.image && item.visible)
        return "请为图片组件选择图片，或先隐藏它。";
    }
    return "";
  }
  const keys: GridKey[] = ["homework", "todo", "timetable"];
  if (!layout?.grid || !layout.modules) return "布局结构不完整。";
  if (
    !["mica_light", "mica_dark"].includes(layout.theme) ||
    !["subtle", "solid"].includes(layout.material) ||
    !["small", "medium", "large"].includes(layout.font) ||
    !["left", "right", "none"].includes(layout.iconMargin) ||
    ![15, 20, 30].includes(layout.interval)
  )
    return "主题、字号或显示选项不受支持。";
  if (
    ["datetime", "homework", "todo", "timetable", "countdown", "message"].some(
      (k) => typeof layout.modules[k as ModuleKey] !== "boolean",
    )
  )
    return "模块显隐配置不完整。";
  for (const key of keys) {
    const r = layout.grid[key];
    if (
      !r ||
      ![r.x, r.y, r.w, r.h].every(Number.isSafeInteger) ||
      r.x < 0 ||
      r.y < 0 ||
      r.w < 5 ||
      r.h < 4 ||
      r.x + r.w > 12 ||
      r.y + r.h > 12
    )
      return "模块超出网格边界或尺寸过小。";
  }
  if (layout.modules.timetable && layout.grid.timetable.h < 7)
    return "课表至少需要 7 格高度。";
  for (let i = 0; i < keys.length; i++)
    for (let j = i + 1; j < keys.length; j++) {
      const a = keys[i]!,
        b = keys[j]!,
        x = layout.grid[a],
        y = layout.grid[b];
      if (
        layout.modules[a] &&
        layout.modules[b] &&
        x.x < y.x + y.w &&
        y.x < x.x + x.w &&
        x.y < y.y + y.h &&
        y.y < x.y + x.h
      )
        return "作业、提醒或课表存在重叠，请调整后发布。";
    }
  return "";
}
