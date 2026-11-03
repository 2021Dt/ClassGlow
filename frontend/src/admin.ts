// 管理后台统一使用北京时间显示期限；日期字段与账号编辑保存的边界一致。
export function adminDate(value: number | string, withTime = false) {
  if (!value) return "—";
  const date = new Date(typeof value === "number" ? value * 1000 : value);
  return new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    ...(withTime
      ? ({ hour: "2-digit", minute: "2-digit", hour12: false } as const)
      : {}),
  }).format(date);
}
export const accountRole = (user: any) =>
  user.systemAdmin
    ? "系统管理员"
    : user.canCreateClasses
      ? "班主任"
      : "普通老师";
export function accountStatus(user: any) {
  if (!user.active) return { label: "已停用", tone: "muted" };
  if (!user.activated) return { label: "待设密码", tone: "amber" };
  if (user.expiresAt && user.expiresAt <= user.serverTime)
    return { label: "已到期", tone: "red" };
  return { label: "正常", tone: "green" };
}
export const auditNames: Record<string, string> = {
  system_initialized: "初始化系统",
  system_settings_changed: "修改站点设置",
  user_created: "创建账号",
  account_created: "创建账号",
  account_invited: "旧版账号邀请",
  account_activated: "激活账号",
  activation_reissued: "重发激活码",
  user_updated: "修改账号",
  password_reset: "重置密码",
  license_issued: "签发高级许可",
  license_redeemed: "兑换高级许可",
  license_revoked: "作废许可码",
  license_ended: "终止高级许可",
  class_updated: "修改班级",
  device_revoked: "解绑教室设备",
  backup_created: "创建备份",
  backup_restored: "恢复备份",
};
export const fileSize = (bytes: number) =>
  bytes >= 1073741824
    ? `${(bytes / 1073741824).toFixed(1)} GB`
    : `${(bytes / 1048576).toFixed(1)} MB`;
