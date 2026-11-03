import { reactive, toRaw } from "vue";
import { emptyModel, type Model } from "./domain";

// 正式内容以服务端为准，不能把上一账号的浏览器缓存当作新账号数据。
export const model = reactive<Model>(emptyModel());
export const localSave = reactive({ failed: false });
export function replaceModel(value: Model) {
  Object.assign(model, value);
}
export function clone<T>(value: T): T {
  return structuredClone(toRaw(value));
}
