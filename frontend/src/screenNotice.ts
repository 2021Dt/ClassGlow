export type NoticeMode = "marquee" | "barrage" | "popup" | "countdown";
export interface NoticeStyle {
  fontFamily: "sans" | "serif" | "kai";
  fontSize: number;
  bold: boolean;
  textColor: string;
  backgroundColor: string;
  opacity: number;
  position: "top" | "center" | "bottom";
  surface: "fullscreen" | "card";
  width: number;
  radius: number;
  flowSeconds: number;
  direction: "left" | "right";
}
export interface ScreenNoticeInput {
  mode: NoticeMode;
  message: string;
  duration: number;
  tone: "blue" | "amber" | "red";
  overFullscreen: boolean;
  style?: NoticeStyle;
}
export const noticeTones = {
  blue: "#17458c",
  amber: "#916013",
  red: "#b72c43",
};
export const noticeModes = [
  { id: "marquee", name: "跑马灯", caption: "横幅滚动" },
  { id: "barrage", name: "弹幕", caption: "悬浮飘过" },
  { id: "popup", name: "全屏弹窗", caption: "全屏或卡片" },
  { id: "countdown", name: "倒计时", caption: "限时任务" },
] as const;
export function defaultNoticeStyle(
  tone: ScreenNoticeInput["tone"] = "blue",
): NoticeStyle {
  return {
    fontFamily: "sans",
    fontSize: 56,
    bold: true,
    textColor: "#ffffff",
    backgroundColor: noticeTones[tone],
    opacity: 95,
    position: "top",
    surface: "fullscreen",
    width: 72,
    radius: 32,
    flowSeconds: 14,
    direction: "left",
  };
}
export function supportsNoticeDesign(version = "") {
  if (!/^\d+\.\d+\.\d+$/.test(version)) return false;
  const [major, minor, patch] = version.split(".").map(Number);
  return major! > 0 || minor! > 4 || (minor === 4 && patch! >= 13);
}
export function validNoticeStyle(value: NoticeStyle) {
  return (
    ["sans", "serif", "kai"].includes(value.fontFamily) &&
    ["top", "center", "bottom"].includes(value.position) &&
    ["fullscreen", "card"].includes(value.surface) &&
    ["left", "right"].includes(value.direction) &&
    typeof value.bold === "boolean" &&
    [value.textColor, value.backgroundColor].every((v) =>
      /^#[\da-f]{6}$/i.test(v),
    ) &&
    [
      [value.fontSize, 24, 120],
      [value.opacity, 20, 100],
      [value.width, 40, 90],
      [value.radius, 0, 64],
      [value.flowSeconds, 6, 40],
    ].every(
      ([v, min, max]) => Number.isInteger(v) && v! >= min! && v! <= max!,
    )
  );
}
export const noticePresets: {
  name: string;
  mode: NoticeMode;
  style: NoticeStyle;
}[] = [
  { name: "课前提示", mode: "marquee", style: defaultNoticeStyle() },
  {
    name: "温柔提醒",
    mode: "popup",
    style: {
      ...defaultNoticeStyle(),
      surface: "card",
      position: "center",
      backgroundColor: "#e7f3ed",
      textColor: "#215544",
      fontFamily: "kai",
      opacity: 100,
    },
  },
  {
    name: "专注计时",
    mode: "countdown",
    style: {
      ...defaultNoticeStyle(),
      surface: "card",
      position: "center",
      backgroundColor: "#172a46",
      fontSize: 64,
      width: 64,
    },
  },
  {
    name: "活力弹幕",
    mode: "barrage",
    style: {
      ...defaultNoticeStyle(),
      backgroundColor: "#7c3aed",
      position: "center",
      radius: 56,
      flowSeconds: 10,
    },
  },
];
export type SavedNoticeStyle = {
  id: string;
  name: string;
  mode: NoticeMode;
  style: NoticeStyle;
};
export function readNoticeStyles(raw: string | null): SavedNoticeStyle[] {
  try {
    if (!raw || raw.length > 40000) return [];
    const values = JSON.parse(raw);
    if (!Array.isArray(values)) return [];
    return values
      .filter(
        (v) =>
          v &&
          typeof v.id === "string" &&
          v.id.length <= 40 &&
          typeof v.name === "string" &&
          v.name.trim() &&
          v.name.length <= 24 &&
          noticeModes.some((m) => m.id === v.mode) &&
          v.style &&
          validNoticeStyle(v.style),
      )
      .slice(0, 12)
      .map((v) => ({
        id: v.id,
        name: v.name,
        mode: v.mode,
        style: Object.fromEntries(
          Object.keys(defaultNoticeStyle()).map((key) => [
            key,
            v.style[key],
          ]),
        ) as unknown as NoticeStyle,
      }));
  } catch {
    return [];
  }
}
export function noticeFill(style: NoticeStyle) {
  const hex = style.backgroundColor;
  return `rgba(${parseInt(hex.slice(1, 3), 16)},${parseInt(hex.slice(3, 5), 16)},${parseInt(hex.slice(5, 7), 16)},${style.opacity / 100})`;
}
