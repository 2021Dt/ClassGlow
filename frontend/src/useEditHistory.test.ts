import { expect, it } from "vitest";
import { useEditHistory } from "./useEditHistory";

it("撤销重做保留独立副本，新修改截断重做，禁用时不消耗历史", () => {
  let value = { text: "初始", nested: { x: 1 } },
    disabled = false;
  const history = useEditHistory(
    () => value,
    (next) => {
      value = next;
    },
    () => disabled,
    2,
  );
  history.checkpoint();
  value = { text: "第二版", nested: { x: 2 } };
  disabled = true;
  history.undo();
  expect(history.history.value).toHaveLength(1);
  disabled = false;
  history.undo();
  expect(value.text).toBe("初始");
  value.nested.x = 100;
  history.redo();
  expect(value.nested.x).toBe(2);
  history.undo();
  history.checkpoint();
  value.text = "新分支";
  history.redo();
  expect(value.text).toBe("新分支");
  history.checkpoint();
  history.checkpoint();
  history.checkpoint();
  expect(history.history.value).toHaveLength(2);
});
