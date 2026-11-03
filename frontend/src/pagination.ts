import type { Content } from "./domain";

export interface Slice {
  id: string;
  subject: string;
  lines: string[];
  continued: boolean;
}
/** 浏览器使用实测字体宽度；Windows 端独立使用 WPF 的真实字体引擎测量。 */
export function wrapText(
  text: string,
  width: number,
  measure: (s: string) => number,
): string[] {
  const lines: string[] = [];
  for (const paragraph of text.split("\n")) {
    if (!paragraph) {
      lines.push("");
      continue;
    }
    let line = "";
    for (const char of Array.from(paragraph)) {
      if (line && measure(line + char) > width) {
        lines.push(line);
        line = char;
      } else line += char;
    }
    lines.push(line);
  }
  return lines;
}
export function paginate(
  items: Content[],
  lineBudget: number,
  width: number,
  measure: (s: string) => number,
): Slice[][] {
  if (lineBudget < 1 || width <= 0)
    throw new Error("当前模块太小，请增大尺寸或减少字号。");
  const pages: Slice[][] = [[]];
  let used = 0;
  for (const item of items) {
    const lines = wrapText(item.body, width, measure);
    let offset = 0;
    while (offset < lines.length) {
      // 条目间留出 0.45 行间距，正文按完整行拆分，避免小模块陷入无法推进的循环。
      let gap = pages.at(-1)!.length ? 0.45 : 0;
      if (lineBudget - used - gap < 1) {
        pages.push([]);
        used = 0;
        gap = 0;
      }
      const take = Math.min(
        lines.length - offset,
        Math.floor(lineBudget - used - gap),
      );
      pages.at(-1)!.push({
        id: item.id,
        subject: item.subject,
        lines: lines.slice(offset, offset + take),
        continued: offset > 0,
      });
      used += take + gap;
      offset += take;
    }
  }
  return pages;
}
