import type { BoardFont, Layout, ShapeKind, ShapePoint } from "./domain";

export const shapeLabels: Record<ShapeKind, string> = {
  rectangle: "矩形",
  ellipse: "圆与椭圆",
  pill: "胶囊",
  triangle: "三角",
  diamond: "菱形",
  hexagon: "六边形",
  star: "星形",
  custom: "自由轮廓",
};

export const defaultOutline: ShapePoint[] = [
  { x: 4, y: 12 },
  { x: 96, y: 12 },
  { x: 76, y: 50 },
  { x: 96, y: 88 },
  { x: 4, y: 88 },
];

const shapeClips: Partial<Record<ShapeKind, string>> = {
  triangle: "polygon(50% 0%, 100% 100%, 0% 100%)",
  diamond: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)",
  hexagon: "polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%)",
  star: "polygon(50% 0%, 61% 35%, 98% 35%, 68% 57%, 79% 94%, 50% 71%, 21% 94%, 32% 57%, 2% 35%, 39% 35%)",
};

export function shapeStyle(
  shape: ShapeKind,
  radius: number,
  vertices = defaultOutline,
) {
  return {
    borderRadius:
      shape === "ellipse"
        ? "50%"
        : shape === "pill"
          ? "9999px"
          : shape === "rectangle"
            ? radius + "px"
            : "0",
    clipPath:
      shape === "custom"
        ? "polygon(evenodd, " +
          vertices.map(({ x, y }) => x + "% " + y + "%").join(",") +
          ")"
        : shapeClips[shape],
  };
}

// 与 Windows 的字体映射使用同一优先顺序；未安装对应中文字体时由系统回退。
export const boardFonts: Record<BoardFont, string> = {
  sans: '"Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", sans-serif',
  serif: 'SimSun, "Songti SC", "Microsoft YaHei", serif',
  kai: 'KaiTi, STKaiti, "Microsoft YaHei", serif',
};

/** 卡片图片独立于桌面背景，遮罩只作用于图片，文字保持完整不透明。 */
export function panelImageStyle(layout: Layout) {
  if (!layout.panelImage) return {};
  const dark = layout.theme === "mica_dark";
  const tint = dark ? "38,41,46" : "249,250,252";
  const shade = `rgba(${tint},${(layout.surfaceOpacity ?? 38) / 100})`;
  return {
    backgroundImage: `linear-gradient(${shade},${shade}),url("${layout.panelImage}")`,
    backgroundSize: `100% 100%,${layout.panelImageFit ?? "cover"}`,
    backgroundPosition: `center,${layout.panelImagePosition ?? "center"}`,
    backgroundRepeat: "no-repeat",
    backgroundColor: dark ? "#2D3036" : "#F6F7F9",
  };
}
