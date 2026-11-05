import { reactive, ref } from "vue";
import type { Draft, Model } from "./domain";
import { defaultLayout, emptyModel, restoreLocalDraft } from "./domain";
import { clearAssetCache, hydrateAssets } from "./assetCache";
import { ASSET_PROTOCOL, SCHEMA } from "./generated/protocol";
import type { ApiDraftInput } from "./generated/api";

// 兼容旧标签页的一次性迁移。有效期由服务端校验，不在浏览器存密码。
const savedToken =
  localStorage.getItem("kanban-session") ??
  sessionStorage.getItem("kanban-session") ??
  "";
if (savedToken) localStorage.setItem("kanban-session", savedToken);
sessionStorage.removeItem("kanban-session");
export const session = reactive({
  token: savedToken,
  userId: "",
  name: "",
  login: "",
  systemAdmin: false,
  canCreateClasses: false,
  expiresAt: 0,
  premiumUntil: 0,
  clockOffset: 0,
});
export function setSession(token: string) {
  if (token !== session.token) {
    clearAssetCache();
    Object.assign(session, {
      userId: "",
      name: "",
      login: "",
      systemAdmin: false,
      canCreateClasses: false,
      expiresAt: 0,
      premiumUntil: 0,
      clockOffset: 0,
    });
  }
  session.token = token;
  if (token) localStorage.setItem("kanban-session", token);
  else localStorage.removeItem("kanban-session");
  sessionStorage.removeItem("kanban-session");
}
window.addEventListener("storage", (event) => {
  if (
    event.key === "kanban-session" &&
    (event.newValue ?? "") !== session.token
  )
    location.reload();
});
export function updateProfile(value: any) {
  session.userId = value.id;
  session.name = value.name;
  session.login = value.login;
  session.systemAdmin = value.systemAdmin;
  session.canCreateClasses = value.canCreateClasses;
  session.expiresAt = value.expiresAt;
  session.premiumUntil = value.premiumUntil;
  session.clockOffset = value.serverTime * 1000 - Date.now();
}
export function premiumAvailable(time = Date.now()) {
  const serverNow = time + session.clockOffset;
  return (
    (!session.expiresAt || session.expiresAt * 1000 > serverNow) &&
    (session.systemAdmin || session.premiumUntil * 1000 > serverNow)
  );
}
const localDraftRevision = ref(0);
export const authNotice = ref<{
  message: string;
  code: string;
  frozenUntil?: number;
  serverTime?: number;
} | null>(null);

export function browserIdentity() {
  // 随机标识只用于异常登录判断，不读取硬件信息，也不是登录凭证。
  const key = "classglow-login-browser";
  try {
    let id = localStorage.getItem(key);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(key, id);
    }
    return id;
  } catch {
    return "";
  }
}
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public code: string,
    public details?: { frozenUntil?: number; serverTime?: number },
    public requestId?: string,
  ) {
    super(message);
  }
}
export async function api<T = any>(
  path: string,
  method = "GET",
  body?: unknown,
  key?: string,
): Promise<T> {
  const abort = new AbortController();
  const timeout = setTimeout(() => abort.abort(), 15000);
  const requestToken = session.token;
  try {
    const response = await fetch(`/api${path}`, {
      method,
      signal: abort.signal,
      headers: {
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(requestToken ? { Authorization: `Bearer ${requestToken}` } : {}),
        "X-ClassGlow-Assets": ASSET_PROTOCOL,
        ...(key ? { "Idempotency-Key": key } : {}),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) {
      const detail = result.detail;
      const fieldMessage = detail?.fields?.[0]?.message?.replace(
        /^Value error, /,
        "",
      );
      const failure = new ApiError(
        (typeof detail === "string"
          ? detail
          : (fieldMessage ?? detail?.message ?? "请求未完成，请稍后重试。")) +
          (response.status >= 500 && response.headers.get("X-Request-ID")
            ? `（问题编号：${response.headers.get("X-Request-ID")}）`
            : ""),
        response.status,
        detail?.code ?? "request_failed",
        detail,
        response.headers.get("X-Request-ID") ?? undefined,
      );
      if (
        requestToken &&
        requestToken === session.token &&
        [
          "account_frozen",
          "session_replaced",
          "session_expired",
          "account_expired",
        ].includes(failure.code)
      ) {
        authNotice.value = {
          message: failure.message,
          code: failure.code,
          ...detail,
        };
        setSession("");
      }
      throw failure;
    }
    return result as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(
      "网络请求未确认，输入已保留。发布请查询原请求结果后再操作。",
      0,
      "network_unknown",
    );
  } finally {
    clearTimeout(timeout);
  }
}
export async function refreshModel(): Promise<Model> {
  const requestToken = session.token;
  const value = await api("/bootstrap");
  if (
    value.schema !== SCHEMA ||
    !value.user?.id ||
    !["classes", "contents", "drafts", "devices", "commands"].every((k) =>
      Array.isArray(value[k]),
    )
  ) {
    throw new ApiError(
      "服务数据结构不兼容，当前页面保留原数据，请联系管理员更新。",
      502,
      "schema_incompatible",
    );
  }
  value.classes = await Promise.all(
    value.classes.map(async (c: any) => ({
      ...c,
      layout: {
        ...defaultLayout(),
        ...(await hydrateAssets(c.layout, c.id, requestToken)),
        modules: { ...defaultLayout().modules, ...c.layout.modules },
        grid: { ...defaultLayout().grid, ...c.layout.grid },
      },
    })),
  );
  value.drafts = value.drafts.map((d: Draft) => ({
    ...d,
    priority: d.priority ?? false,
  }));
  if (requestToken !== session.token)
    throw new ApiError(
      "登录状态已改变，请刷新当前页面。",
      409,
      "session_changed",
    );
  updateProfile(value.user);
  return {
    ...emptyModel(),
    ...value,
    selected:
      localStorage.getItem(`kanban-class-${session.userId}`) ??
      value.classes[0]?.id ??
      "",
  };
}
export function localDraftKey(id: string) {
  return `kanban-input-${session.userId}-${id}`;
}
export function persistInput(draft: Draft) {
  localStorage.setItem(localDraftKey(draft.id), JSON.stringify(draft));
  localDraftRevision.value++;
}
export function removeInput(id: string) {
  localStorage.removeItem(localDraftKey(id));
  localDraftRevision.value++;
}
export function pendingInputs(): Draft[] {
  void localDraftRevision.value;
  // 本机副本按账号隔离；其他教师登录不能在界面中读到前一人的输入。
  const prefix = `kanban-input-${session.userId}-`;
  return Object.keys(localStorage)
    .filter((k) => k.startsWith(prefix))
    .flatMap((k) => {
      try {
        const d = restoreLocalDraft(
          JSON.parse(localStorage.getItem(k) ?? "null"),
        );
        return d && localDraftKey(d.id) === k ? [d] : [];
      } catch {
        return [];
      }
    });
}
export async function saveCloudDraft(draft: Draft): Promise<Draft> {
  const payload: ApiDraftInput = draft;
  const value = await api(`/drafts/${draft.id}`, "PUT", payload);
  if (
    value.id !== draft.id ||
    !Number.isSafeInteger(value.revision) ||
    value.revision <= draft.revision ||
    !Number.isFinite(Date.parse(value.updated))
  ) {
    throw new ApiError(
      "草稿保存回执不完整，已保留本机输入，请查询最新草稿。",
      502,
      "draft_receipt_invalid",
    );
  }
  return value;
}
