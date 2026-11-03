import { reactive } from "vue";
import { emptyModel, type Model } from "./domain";

// 正式内容以服务端为准，不能把上一账号的浏览器缓存当作新账号数据。
export const model = reactive<Model>(emptyModel());
export const localSave = reactive({ failed: false });
export function replaceModel(value: Model) {
  Object.assign(model, value);
}
export function clone<T>(value: T): T {
  // 业务状态与接口共用 JSON 结构；局部编辑可能混入嵌套响应式对象，统一序列化后再复制。
  return JSON.parse(JSON.stringify(value));
}
