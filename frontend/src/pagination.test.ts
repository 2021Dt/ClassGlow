import { describe, expect, it } from "vitest";
import {
  defaultUntil,
  dateKey,
  defaultLayout,
  layoutIssue,
  visible,
  type Content,
} from "./domain";
import { paginate, wrapText } from "./pagination";

describe("内容完整性与日期边界", () => {
  it("只有一行容量的小模块也能推进分页，不丢失正文", () => {
    const result = paginate(
      [{ id: "tiny", subject: "数学", body: "一二三四五六七八" } as Content],
      1,
      2,
      (s) => Array.from(s).length,
    );
    expect(result).toHaveLength(4);
    expect(
      result
        .flat()
        .flatMap((s) => s.lines)
        .join(""),
    ).toBe("一二三四五六七八");
  });
  it("网格重叠、越界与未知主题不能进入发布", () => {
    const layout = defaultLayout();
    expect(layoutIssue(layout)).toBe("");
    layout.grid.todo.x = 0;
    expect(layoutIssue(layout)).toContain("重叠");
    layout.grid.todo.x = 7;
    layout.grid.todo.w = 6;
    expect(layoutIssue(layout)).toContain("边界");
  });
  it("500 字长文分页后逐字保留，包含空行和 Unicode 字符", () => {
    const text = `${"测".repeat(490)}\n\n最后一句🙂`;
    const lines = wrapText(text, 12, (s) => Array.from(s).length);
    expect(lines.join("\n").replaceAll("\n", "")).toBe(
      text.replaceAll("\n", ""),
    );
    const pages = paginate(
      [{ id: "one", subject: "语文", body: text } as Content],
      9,
      12,
      (s) => Array.from(s).length,
    );
    expect(pages.length).toBeGreaterThan(1);
    expect(
      pages
        .flat()
        .flatMap((s) => s.lines)
        .join(""),
    ).toBe(text.replaceAll("\n", ""));
    expect(pages[1]![0]!.continued).toBe(true);
  });
  it("未来日期默认结束跟随开始所在日期，使用北京时间", () => {
    expect(dateKey("2026-11-02T20:30:00Z")).toBe("2026-11-03");
    expect(defaultUntil("2026-11-02T20:30:00Z", "homework")).toBe(
      "2026-11-04T08:00:00+08:00",
    );
  });
  it("开始包含，结束不包含，完成与撤回不展示", () => {
    const c = {
      state: "published",
      from: "2026-11-02T00:00:00+08:00",
      until: "2026-11-03T08:00:00+08:00",
    } as Content;
    expect(visible(c, Date.parse(c.from))).toBe(true);
    expect(visible(c, Date.parse(c.until))).toBe(false);
    expect(visible({ ...c, state: "withdrawn" }, Date.parse(c.from))).toBe(
      false,
    );
  });
});
