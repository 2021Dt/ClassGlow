import type { Content } from "./domain";

export interface Slice {
  id: string;
  subject: string;
  lines: string[];
  continued: boolean;
  showSubject: boolean;
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
        const glyphs = Array.from(line),
          last = glyphs.at(-1)!;
        // 在卡片边界前移一个字，避免句号独占行首或左括号停在行末。
        const carry =
          glyphs.length > 1 &&
          ("，。！？；：、）】》”’,.!?;:)]}".includes(char) ||
            "（【《“‘([{".includes(last)) &&
          measure(last + char) <= width;
        lines.push(carry ? glyphs.slice(0, -1).join("") : line);
        line = carry ? last + char : char;
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
  keepWhole = false,
  subjectLabels = false,
): Slice[][] {
  if (lineBudget < 1 || width <= 0)
    throw new Error("当前模块太小，请增大尺寸或减少字号。");
  const pages: Slice[][] = [[]];
  let used = 0;
  for (const item of items) {
    const lines = wrapText(item.body, width, measure);
    // 学科独占一行标签；极小组件优先保证正文至少显示一整行。
    const label = subjectLabels && item.subject && lineBudget >= 1.9 ? 0.9 : 0;
    // 一页能放下的作业直接移到下一页，长作业继续按行拆分。
    if (
      keepWhole &&
      used &&
      lines.length + label <= lineBudget &&
      used + 0.45 + label + lines.length > lineBudget
    ) {
      pages.push([]);
      used = 0;
    }
    let offset = 0;
    while (offset < lines.length) {
      // 条目间留出 0.45 行间距，正文按完整行拆分，避免小模块陷入无法推进的循环。
      let gap = pages.at(-1)!.length ? 0.45 : 0;
      if (lineBudget - used - gap - label < 1) {
        pages.push([]);
        used = 0;
        gap = 0;
      }
      const take = Math.min(
        lines.length - offset,
        Math.floor(lineBudget - used - gap - label + 1e-9),
      );
      pages.at(-1)!.push({
        id: item.id,
        subject: item.subject,
        lines: lines.slice(offset, offset + take),
        continued: offset > 0,
        showSubject: label > 0,
      });
      used += take + gap + label;
      offset += take;
    }
  }
  return pages;
}
