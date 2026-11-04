import type { Device } from "./domain";

export function deviceStatus(device: Device, version: number) {
  if (!device.online) return "离线，等待联网";
  if (device.localPaused || device.remotePaused) return "展示已暂停";
  if (device.error) return device.error;
  if (device.cached < version) return "正在接收更新";
  if (device.deferred) return "已接收，等待全屏结束";
  if (device.applyState === "waiting") return "请在电脑启用展示";
  if (device.applied < version) return "已接收，正在准备画面";
  return device.applied ? "已显示最新内容" : "等待首次展示";
}
