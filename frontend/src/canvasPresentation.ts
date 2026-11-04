import type { CanvasWidget, Classroom, Content, Layout } from "./domain";
import { dateKey, timeLabel } from "./domain";
import { paginate, type Slice } from "./pagination";
import { boardFonts } from "./boardAppearance";

const compare = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);

export const textTokens = [
  "班级",
  "日期",
  "时间",
  "作业数",
  "活动",
  "倒计时",
  "寄语",
] as const;

function resolveText(
  item: CanvasWidget,
  classroom: Classroom,
  contents: Content[],
  now: number,
) {
  if (!item.dynamicText) return item.text;
  const day = dateKey(now);
  const days = Math.round(
    (Date.parse(classroom.eventDate) - Date.parse(day)) / 86400000,
  );
  const values: Record<(typeof textTokens)[number], string> = {
    班级: classroom.name,
    日期: day,
    时间: timeLabel(now),
    作业数: String(
      contents.filter((entry) => entry.kind === "homework").length,
    ),
    活动: classroom.event || "—",
    倒计时: classroom.event && days >= 0 ? `${days} 天` : "—",
    寄语: classroom.message || "—",
  };
  // 一次替换，班级数据本身按纯文本展示，不再解释其中的占位符。
  return item.text.replace(
    /\{\{(班级|日期|时间|作业数|活动|倒计时|寄语)\}\}/g,
    (_, key: keyof typeof values) => values[key],
  );
}

export function canvasPages(
  layout: Layout,
  classroom: Classroom,
  contents: Content[],
  context: CanvasRenderingContext2D,
  now: number,
): Record<string, Slice[][]> {
  return Object.fromEntries(
    (layout.canvas ?? [])
      .filter((item) => item.visible)
      .map((item) => {
        context.font = `${item.fontWeight ?? 400} ${item.fontSize}px ${boardFonts[item.fontFamily ?? "sans"]}`;
        return [
          item.id,
          widgetPages(
            item,
            classroom,
            contents,
            item.w,
            (line) => context.measureText(line).width,
            now,
          ),
        ];
      }),
  );
}

export function canvasPageCount(pages: Record<string, Slice[][]>) {
  return Math.max(1, ...Object.values(pages).map((value) => value.length));
}

/** 数据组件与自由文字统一逐行分页，绘画板改变尺寸时仍保留完整正文。 */
export function widgetPages(
  item: CanvasWidget,
  classroom: Classroom,
  contents: Content[],
  width: number,
  measure: (s: string) => number,
  now: number,
): Slice[][] {
  let entries: Content[] = [];
  if (item.kind === "homework" || item.kind === "todo")
    entries = contents
      .filter(
        (c) =>
          c.kind === item.kind &&
          (item.kind !== "homework" ||
            !item.subjectFilter ||
            c.subject === item.subjectFilter),
      )
      .sort(
        (a, b) =>
          Number(!!b.priority) - Number(!!a.priority) ||
          (item.kind === "homework"
            ? compare(a.subject, b.subject) ||
              compare(b.created, a.created) ||
              compare(a.id, b.id)
            : 0),
      );
  else if (item.kind === "timetable")
    entries = classroom.timetable.map(
      (name, index) =>
        ({
          id: `course-${index}`,
          subject: "",
          body: `${index + 1}  ${name}`,
        }) as Content,
    );
  else if (item.kind === "text" || item.kind === "message")
    entries = [
      {
        id: item.id,
        subject: "",
        body:
          item.kind === "text"
            ? resolveText(item, classroom, contents, now)
            : item.dataSource === "custom"
              ? item.text
              : classroom.message,
      } as Content,
    ];
  const budget = Math.max(
    1,
    (item.h -
      (item.kind === "text" || item.showTitle === false
        ? 32
        : 46 + (item.titleSize ?? 28) * 1.5)) /
      (item.fontSize * (item.lineHeight ?? 1.5)),
  );
  return paginate(
    entries,
    budget,
    Math.max(
      1,
      width - (item.kind === "text" || item.showTitle === false ? 32 : 48),
    ),
    measure,
    item.kind === "homework",
    item.kind === "homework" && !item.subjectFilter,
  );
}
