import type { CanvasWidget, Layout, WidgetKind } from "./domain";
import { clone } from "./store";
export const CANVAS_WIDTH = 1600,
  CANVAS_HEIGHT = 900;
export const widgetLabels: Record<WidgetKind, string> = {
  homework: "今日作业",
  todo: "班级提醒",
  timetable: "今日课表",
  datetime: "班级与时钟",
  countdown: "事件倒计时",
  message: "班级寄语",
  text: "自由文字",
  image: "图片",
  shape: "图形",
};
export function widgetMinimum(kind: WidgetKind, showTitle = true) {
  return ["image", "shape"].includes(kind)
    ? { w: 48, h: 48 }
    : kind === "text"
      ? { w: 120, h: 100 }
      : {
          w: 280,
          h:
            showTitle || kind === "datetime" || kind === "countdown"
              ? 180
              : 100,
        };
}
export function newWidget(
  kind: WidgetKind,
  patch: Partial<CanvasWidget> = {},
): CanvasWidget {
  return {
    id: crypto.randomUUID(),
    kind,
    x: 40,
    y: 40,
    w: 480,
    h: kind === "homework" ? 500 : kind === "timetable" ? 380 : 220,
    title: widgetLabels[kind],
    text: kind === "text" ? "在这里写下你的灵感" : "",
    image: "",
    visible: true,
    locked: false,
    fontSize: 32,
    fontFamily: "sans",
    fontWeight: 400,
    lineHeight: 1.5,
    titleSize: 28,
    backgroundImage: "",
    backgroundFit: "cover",
    backgroundPosition: "center",
    textColor: "",
    fillColor: kind === "shape" ? "#A5C7B7" : "#FFFFFF",
    surface: kind === "text" ? "none" : kind === "shape" ? "solid" : "glass",
    opacity: kind === "shape" ? 45 : 18,
    radius: 24,
    align: "left",
    ...patch,
  };
}
export type CanvasTemplate =
  "standard" | "bento" | "columns" | "notes" | "blank";
export function canvasTemplate(name: CanvasTemplate): CanvasWidget[] {
  if (name === "blank") return [];
  const item = (kind: WidgetKind, x: number, y: number, w: number, h: number) =>
    newWidget(kind, { x, y, w, h });
  // 参考 Magic UI Bento Grid：主内容占大格，辅助信息按等距小格组合。
  if (name === "bento")
    return [
      item("homework", 40, 32, 856, 828),
      item("datetime", 920, 32, 640, 210),
      item("todo", 920, 266, 640, 230),
      item("timetable", 920, 520, 304, 340),
      item("countdown", 1248, 520, 312, 184),
      newWidget("message", {
        x: 1248,
        y: 728,
        w: 312,
        h: 132,
        showTitle: false,
        fontSize: 24,
      }),
    ].map((widget) => ({ ...widget, radius: 28, opacity: 22 }));
  if (name === "columns")
    return [
      item("datetime", 40, 32, 496, 190),
      item("countdown", 552, 32, 496, 190),
      item("message", 1064, 32, 496, 190),
      item("homework", 40, 246, 496, 614),
      item("todo", 552, 246, 496, 614),
      item("timetable", 1064, 246, 496, 614),
    ];
  if (name === "notes")
    return [
      item("datetime", 40, 32, 404, 190),
      item("homework", 468, 32, 648, 828),
      item("todo", 1140, 32, 420, 366),
      item("timetable", 40, 246, 404, 614),
      item("message", 1140, 422, 420, 224),
      item("countdown", 1140, 670, 420, 190),
    ];
  return [
    item("datetime", 40, 32, 680, 190),
    item("countdown", 744, 32, 388, 190),
    item("message", 1156, 32, 404, 190),
    item("homework", 40, 246, 828, 614),
    item("todo", 892, 246, 668, 252),
    item("timetable", 892, 522, 668, 338),
  ];
}
export function toCanvas(layout: Layout): Layout {
  if (layout.editor === "canvas") return clone(layout);
  const canvas = canvasTemplate("standard").filter(
    (item) => layout.modules[item.kind as keyof Layout["modules"]],
  );
  return { ...clone(layout), editor: "canvas", canvas };
}

/** 试排只改组件位置与尺寸；自定义组合或锁定组件留给教师自己安排。 */
export function homeworkPriorityLayout(layout: Layout): Layout | null {
  const items = layout.canvas ?? [];
  const visible = items.filter((item) => item.visible);
  type Placement = Pick<CanvasWidget, "x" | "y" | "w" | "h">;
  const slots: Partial<Record<WidgetKind, Placement>> = {
    homework: { x: 0, y: 0, w: visible.length === 1 ? 1600 : 980, h: 900 },
    datetime: { x: 1000, y: 0, w: 290, h: 180 },
    countdown: { x: 1310, y: 0, w: 290, h: 180 },
    message: { x: 1000, y: 200, w: 600, h: 180 },
    todo: { x: 1000, y: 400, w: 600, h: 180 },
    timetable: { x: 1000, y: 600, w: 600, h: 300 },
  };
  const positions = new Map<string, Placement>();
  const placed = new Set<WidgetKind>();
  for (const item of visible) {
    const slot = slots[item.kind];
    if (!slot || item.locked || item.groupId || placed.has(item.kind))
      return null;
    const fitted = clampWidget({ ...item, ...slot });
    if (fitted.w !== slot.w || fitted.h !== slot.h) return null;
    positions.set(item.id, slot);
    placed.add(item.kind);
  }
  return {
    ...layout,
    canvas: items.map((item) => ({ ...item, ...positions.get(item.id) })),
  };
}
export function clampWidget(item: CanvasWidget): CanvasWidget {
  item = {
    ...item,
    fontSize: Math.max(16, Math.min(96, item.fontSize)),
    titleSize: Math.max(16, Math.min(64, item.titleSize ?? 28)),
    lineHeight: Math.max(1.2, Math.min(2, item.lineHeight ?? 1.5)),
    opacity: Math.max(0, Math.min(100, item.opacity)),
    radius: Math.max(0, Math.min(80, item.radius)),
  };
  const min = widgetMinimum(item.kind, item.showTitle);
  if (item.kind === "text") {
    min.w = Math.max(min.w, item.fontSize + 32);
    min.h = Math.max(min.h, Math.ceil(item.fontSize * item.lineHeight! + 32));
  }
  if (["homework", "todo", "timetable", "message"].includes(item.kind))
    min.h = Math.max(
      min.h,
      Math.ceil(
        (item.showTitle === false ? 32 : 46 + item.titleSize! * 1.5) +
          item.fontSize * item.lineHeight!,
      ),
    );
  if (["datetime", "countdown"].includes(item.kind))
    min.h = Math.max(min.h, Math.ceil(105 + (item.fontSize + 24) * 1.2));
  const w = Math.max(min.w, Math.min(CANVAS_WIDTH, Math.round(item.w))),
    h = Math.max(min.h, Math.min(CANVAS_HEIGHT, Math.round(item.h)));
  return {
    ...item,
    w,
    h,
    x: Math.max(0, Math.min(CANVAS_WIDTH - w, Math.round(item.x))),
    y: Math.max(0, Math.min(CANVAS_HEIGHT - h, Math.round(item.y))),
  };
}

export type ResizeEdge = "n" | "s" | "e" | "w" | "nw" | "ne" | "sw" | "se";
/** 从任意边或角拉伸，固定相对的边，并保留至少一行内容所需的尺寸。 */
export function resizeCanvasItem(
  item: CanvasWidget,
  edge: ResizeEdge,
  dx: number,
  dy: number,
) {
  const minimum = clampWidget({ ...item, w: 0, h: 0 });
  let left = item.x,
    top = item.y,
    right = item.x + item.w,
    bottom = item.y + item.h;
  if (edge.includes("w"))
    left = Math.max(0, Math.min(right - minimum.w, left + dx));
  if (edge.includes("e"))
    right = Math.min(1600, Math.max(left + minimum.w, right + dx));
  if (edge.includes("n"))
    top = Math.max(0, Math.min(bottom - minimum.h, top + dy));
  if (edge.includes("s"))
    bottom = Math.min(900, Math.max(top + minimum.h, bottom + dy));
  return { ...item, x: left, y: top, w: right - left, h: bottom - top };
}

/** 用同一个位移移动整组，碰到画布边缘时保持组内相对位置。 */
export function moveCanvasItems(items: CanvasWidget[], dx: number, dy: number) {
  const left = Math.min(...items.map((item) => item.x));
  const top = Math.min(...items.map((item) => item.y));
  const right = Math.max(...items.map((item) => item.x + item.w));
  const bottom = Math.max(...items.map((item) => item.y + item.h));
  const x = Math.max(-left, Math.min(CANVAS_WIDTH - right, dx));
  const y = Math.max(-top, Math.min(CANVAS_HEIGHT - bottom, dy));
  return items.map((item) => ({ ...item, x: item.x + x, y: item.y + y }));
}
export async function widgetImage(file: File, large = false) {
  if (
    !["image/jpeg", "image/png", "image/webp"].includes(file.type) ||
    file.size > 8 * 1024 * 1024
  )
    throw new Error("请选择 8 MB 以内的 JPG、PNG 或 WebP 图片。");
  const bitmap = await createImageBitmap(file);
  try {
    const scale = Math.min(
        1,
        (large ? 1920 : 1280) / bitmap.width,
        (large ? 1080 : 960) / bitmap.height,
      ),
      canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    const ctx = canvas.getContext("2d")!;
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    for (const quality of [0.8, 0.65, 0.5, 0.35]) {
      const data = canvas.toDataURL("image/jpeg", quality);
      if (data.length <= (large ? 500000 : 240000)) return data;
    }
    throw new Error("图片细节较多，请换一张更小的图片。");
  } finally {
    bitmap.close();
  }
}
