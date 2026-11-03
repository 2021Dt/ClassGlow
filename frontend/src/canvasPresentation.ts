import type { CanvasWidget, Classroom, Content } from "./domain";
import { paginate, type Slice } from "./pagination";
/** 数据组件与自由文字统一逐行分页，绘画板改变尺寸时仍保留完整正文。 */
export function widgetPages(
  item: CanvasWidget,
  classroom: Classroom,
  contents: Content[],
  width: number,
  measure: (s: string) => number,
): Slice[][] {
  let entries: Content[] = [];
  if (item.kind === "homework" || item.kind === "todo")
    entries = contents
      .filter((c) => c.kind === item.kind)
      .sort((a, b) => Number(!!b.priority) - Number(!!a.priority))
      .map((c) => ({
        ...c,
        body: item.kind === "homework" ? `${c.subject} · ${c.body}` : c.body,
      }));
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
        body: item.kind === "text" ? item.text : classroom.message,
      } as Content,
    ];
  const budget = Math.max(
    1,
    (item.h - (item.kind === "text" ? 32 : 88)) / (item.fontSize * 1.5),
  );
  return paginate(
    entries,
    budget,
    Math.max(1, width - (item.kind === "text" ? 32 : 48)),
    measure,
  );
}
