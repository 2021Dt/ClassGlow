import { expect, it } from "vitest";
import {
  readRecordFilters,
  readWorkspaceRoute,
  recordFiltersSearch,
  workspaceSearch,
} from "./workspaceRoute";

it("班级、页面和筛选条件往返保留，邀请参数不丢失", () => {
  let search = workspaceSearch("?invite=temporary", "contents", "class-a");
  search = recordFiltersSearch(search, {
    kind: "homework",
    state: "ended",
    subject: "数学",
    date: "2026-11-05",
    search: "第3页",
    offset: 20,
  });
  expect(readWorkspaceRoute(search)).toEqual({
    page: "contents",
    classId: "class-a",
  });
  expect(readRecordFilters(search)).toEqual({
    kind: "homework",
    state: "ended",
    subject: "数学",
    date: "2026-11-05",
    search: "第3页",
    offset: 20,
  });
  expect(new URLSearchParams(search).get("invite")).toBe("temporary");
});
it("外部链接中的未知页面和无效筛选值使用安全默认值", () => {
  expect(readWorkspaceRoute("?page=unknown").page).toBe("");
  expect(
    readRecordFilters(
      "?records_kind=unknown&records_offset=-1&records_date=invalid",
    ),
  ).toMatchObject({ kind: "", date: "", offset: 0 });
});
