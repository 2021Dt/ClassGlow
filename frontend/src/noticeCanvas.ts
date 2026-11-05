import { widgetImage } from "./canvas";

export type NoticeElementKind = "text" | "card" | "image" | "countdown";
export interface NoticeElement {
  id: string;
  kind: NoticeElementKind;
  x: number;
  y: number;
  w: number;
  h: number;
  text: string;
  fontFamily: "sans" | "serif" | "kai";
  fontSize: number;
  bold: boolean;
  align: "left" | "center" | "right";
  textColor: string;
  fillColor: string;
  opacity: number;
  radius: number;
  image: string;
  imageFit: "cover" | "contain";
  hidden: boolean;
  locked: boolean;
}
export interface NoticeCanvas {
  backgroundColor: string;
  backgroundOpacity: number;
  elements: NoticeElement[];
}
export const elementNames = {
  text: "文字",
  card: "卡片",
  image: "图片",
  countdown: "倒计时",
};
export const CANVAS_BUDGET = 524288;
export function newNoticeElement(
  kind: NoticeElementKind,
  patch: Partial<NoticeElement> = {},
): NoticeElement {
  return {
    id: crypto.randomUUID(),
    kind,
    x: 400,
    y: 330,
    w: 800,
    h: 240,
    text:
      kind === "card"
        ? "课前提醒\n请整理桌面，准备上课。"
        : kind === "text"
          ? "输入提醒文字"
          : "",
    fontFamily: "sans",
    fontSize: kind === "countdown" ? 120 : 56,
    bold: true,
    align: "center",
    textColor: "#ffffff",
    fillColor: "#215544",
    opacity: 95,
    radius: 32,
    image: "",
    imageFit: "contain",
    hidden: false,
    locked: false,
    ...patch,
  };
}
export function defaultNoticeCanvas(): NoticeCanvas {
  return {
    backgroundColor: "#102a43",
    backgroundOpacity: 35,
    elements: [newNoticeElement("card", { x: 300, y: 250, w: 1000, h: 400 })],
  };
}
export function supportsNoticeCanvas(version = "") {
  if (!/^\d+\.\d+\.\d+$/.test(version)) return false;
  const [major, minor, patch] = version.split(".").map(Number);
  return major! > 0 || minor! > 4 || (minor === 4 && patch! >= 14);
}
export function rgba(hex: string, opacity: number) {
  return `rgba(${parseInt(hex.slice(1, 3), 16)},${parseInt(hex.slice(3, 5), 16)},${parseInt(hex.slice(5, 7), 16)},${opacity / 100})`;
}
export function canvasBytes(value: NoticeCanvas) {
  return new TextEncoder().encode(JSON.stringify(value)).length;
}
export function canvasProblem(value: NoticeCanvas): string {
  const color = (v: unknown) =>
    typeof v === "string" && /^#[\da-f]{6}$/i.test(v);
  const number = (v: unknown, min: number, max: number) =>
    typeof v === "number" && Number.isInteger(v) && v >= min && v <= max;
  if (
    !value ||
    !color(value.backgroundColor) ||
    !number(value.backgroundOpacity, 0, 100) ||
    !Array.isArray(value.elements)
  )
    return "画布格式无法识别。";
  if (!value.elements.length || value.elements.length > 12)
    return "请添加 1 至 12 个组件。";
  if (new Set(value.elements.map((e) => e.id)).size !== value.elements.length)
    return "组件标识重复。";
  for (const e of value.elements) {
    if (
      !e ||
      typeof e.id !== "string" ||
      !e.id.length ||
      e.id.length > 40 ||
      !Object.hasOwn(elementNames, e.kind) ||
      !number(e.x, 0, 1552) ||
      !number(e.y, 0, 852) ||
      !number(e.w, 48, 1600) ||
      !number(e.h, 48, 900) ||
      e.x + e.w > 1600 ||
      e.y + e.h > 900 ||
      typeof e.text !== "string" ||
      e.text.length > 240 ||
      !number(e.fontSize, 24, 160) ||
      !["sans", "serif", "kai"].includes(e.fontFamily) ||
      !["left", "center", "right"].includes(e.align) ||
      !color(e.textColor) ||
      !color(e.fillColor) ||
      !number(e.opacity, 0, 100) ||
      !number(e.radius, 0, 120) ||
      ![e.bold, e.hidden, e.locked].every((v) => typeof v === "boolean") ||
      !["cover", "contain"].includes(e.imageFit) ||
      typeof e.image !== "string" ||
      (e.kind === "image"
        ? !/^data:image\/jpeg;base64,[A-Za-z0-9+/]+=*$/.test(e.image) ||
          e.image.length > 240000
        : !!e.image)
    )
      return "有组件内容或尺寸不正确，请检查。";
  }
  if (
    !value.elements.some(
      (e) =>
        !e.hidden &&
        (e.kind === "image" ||
          e.kind === "countdown" ||
          e.text.trim() ||
          (e.kind === "card" && e.opacity > 0)),
    )
  )
    return "至少保留一个有内容的可见组件。";
  if (canvasBytes(value) > CANVAS_BUDGET)
    return "画布超过 512 KB，请删除或更换较小的图片。";
  return "";
}
export function clampNoticeElement(e: NoticeElement): NoticeElement {
  const w = Math.max(48, Math.min(1600, Math.round(e.w))),
    h = Math.max(48, Math.min(900, Math.round(e.h)));
  return {
    ...e,
    w,
    h,
    x: Math.max(0, Math.min(1600 - w, Math.round(e.x))),
    y: Math.max(0, Math.min(900 - h, Math.round(e.y))),
  };
}
export function resizeNoticeElement(
  e: NoticeElement,
  edge: string,
  dx: number,
  dy: number,
): NoticeElement {
  let l = e.x,
    r = e.x + e.w,
    t = e.y,
    b = e.y + e.h;
  if (edge.includes("w")) l = Math.max(0, Math.min(r - 48, l + dx));
  if (edge.includes("e")) r = Math.min(1600, Math.max(l + 48, r + dx));
  if (edge.includes("n")) t = Math.max(0, Math.min(b - 48, t + dy));
  if (edge.includes("s")) b = Math.min(900, Math.max(t + 48, b + dy));
  return clampNoticeElement({ ...e, x: l, y: t, w: r - l, h: b - t });
}
export const noticeImage = (file: File) => widgetImage(file);
export interface SavedNoticeCanvas {
  id: string;
  name: string;
  canvas: NoticeCanvas;
}
export function readNoticeCanvases(raw: string | null): SavedNoticeCanvas[] {
  try {
    if (!raw || raw.length > CANVAS_BUDGET * 3 + 1000) return [];
    const list = JSON.parse(raw);
    if (!Array.isArray(list)) return [];
    return list
      .filter(
        (v) =>
          v &&
          typeof v.id === "string" &&
          v.id.length <= 40 &&
          typeof v.name === "string" &&
          v.name.trim() &&
          v.name.length <= 24 &&
          !canvasProblem(v.canvas),
      )
      .slice(0, 3);
  } catch {
    return [];
  }
}
