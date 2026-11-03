import { expect, it } from "vitest";
import { reactive } from "vue";
import { defaultLayout } from "./domain";
import { clone } from "./store";

it("上传背景并局部调整布局后，发布副本保留配置且与预览独立", () => {
  const original = reactive(defaultLayout());
  const edited = reactive({
    ...original,
    backgroundMode: "color" as const,
    backgroundColor: "#AABBCC",
  });
  const published = clone(edited);
  expect(published.backgroundColor).toBe("#AABBCC");
  published.grid.homework.w = 6;
  expect(edited.grid.homework.w).toBe(7);
});
