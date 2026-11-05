import { shallowRef } from "vue";
import { clone } from "./store";

/** 编辑事务由调用者决定何时 checkpoint；拖动过程中不重复复制大图。 */
export function useEditHistory<T>(
  read: () => T,
  write: (value: T) => void,
  disabled: () => boolean,
  limit = 20,
) {
  const history = shallowRef<T[]>([]),
    future = shallowRef<T[]>([]);
  function checkpoint() {
    if (disabled()) return;
    history.value = [...history.value, clone(read())].slice(-limit);
    future.value = [];
  }
  function undo() {
    if (disabled() || !history.value.length) return;
    const previous = history.value[history.value.length - 1]!;
    future.value = [...future.value, clone(read())].slice(-limit);
    history.value = history.value.slice(0, -1);
    write(clone(previous));
  }
  function redo() {
    if (disabled() || !future.value.length) return;
    const next = future.value[future.value.length - 1]!;
    history.value = [...history.value, clone(read())].slice(-limit);
    future.value = future.value.slice(0, -1);
    write(clone(next));
  }
  return { history, future, checkpoint, undo, redo };
}
