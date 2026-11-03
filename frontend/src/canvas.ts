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
  shape: "装饰色块",
};
export function widgetMinimum(kind: WidgetKind) {
  return ["image", "shape"].includes(kind)
    ? { w: 48, h: 48 }
    : kind === "text"
      ? { w: 120, h: 100 }
      : { w: 280, h: 180 };
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
    textColor: "",
    fillColor: kind === "shape" ? "#A5C7B7" : "#FFFFFF",
    surface: kind === "text" ? "none" : kind === "shape" ? "solid" : "glass",
    opacity: kind === "shape" ? 45 : 18,
    radius: 24,
    align: "left",
    ...patch,
  };
}
export function canvasTemplate(
  name: "standard" | "columns" | "notes" | "blank",
): CanvasWidget[] {
  if (name === "blank") return [];
  const item = (kind: WidgetKind, x: number, y: number, w: number, h: number) =>
    newWidget(kind, { x, y, w, h });
  if (name === "columns")
    return [
      item("datetime", 0, 0, 520, 180),
      item("countdown", 540, 0, 520, 180),
      item("message", 1080, 0, 520, 180),
      item("homework", 0, 200, 520, 700),
      item("todo", 540, 200, 520, 700),
      item("timetable", 1080, 200, 520, 700),
    ];
  if (name === "notes")
    return [
      item("datetime", 0, 0, 500, 200),
      item("homework", 520, 0, 560, 900),
      item("todo", 1100, 0, 500, 440),
      item("timetable", 0, 220, 500, 680),
      item("message", 1100, 460, 500, 220),
      item("countdown", 1100, 700, 500, 200),
    ];
  return [
    item("datetime", 0, 0, 680, 180),
    item("countdown", 700, 0, 420, 180),
    item("message", 1140, 0, 460, 180),
    item("homework", 0, 200, 840, 700),
    item("todo", 860, 200, 740, 260),
    item("timetable", 860, 480, 740, 420),
  ];
}
export function toCanvas(layout: Layout): Layout {
  if (layout.editor === "canvas") return clone(layout);
  const canvas = canvasTemplate("standard").filter(
    (item) => layout.modules[item.kind as keyof Layout["modules"]],
  );
  return { ...clone(layout), editor: "canvas", canvas };
}
export function clampWidget(item: CanvasWidget): CanvasWidget {
  item = {
    ...item,
    fontSize: Math.max(16, Math.min(56, item.fontSize)),
    opacity: Math.max(0, Math.min(100, item.opacity)),
    radius: Math.max(0, Math.min(80, item.radius)),
  };
  const min = widgetMinimum(item.kind);
  if (item.kind === "text")
    min.h = Math.max(min.h, Math.ceil(item.fontSize * 1.5 + 32));
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
export async function widgetImage(file: File) {
  if (
    !["image/jpeg", "image/png", "image/webp"].includes(file.type) ||
    file.size > 8 * 1024 * 1024
  )
    throw new Error("请选择 8 MB 以内的 JPG、PNG 或 WebP 图片。");
  const bitmap = await createImageBitmap(file);
  try {
    const scale = Math.min(1, 1280 / bitmap.width, 960 / bitmap.height),
      canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    const ctx = canvas.getContext("2d")!;
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    for (const quality of [0.8, 0.65, 0.5, 0.35]) {
      const data = canvas.toDataURL("image/jpeg", quality);
      if (data.length <= 240000) return data;
    }
    throw new Error("图片细节较多，请换一张更小的图片。");
  } finally {
    bitmap.close();
  }
}
