<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import {
  LayoutDashboard,
  BookOpen,
  FilePenLine,
  Monitor,
  Palette,
  Settings,
  ChevronDown,
  Plus,
  Bell,
  ArrowUpRight,
  Check,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  LogOut,
  X,
  Users,
  ShieldCheck,
  School,
  ArrowRight,
} from "@lucide/vue";
import BoardPreview from "./components/BoardPreview.vue";
import EditorSheet from "./components/EditorSheet.vue";
import BoardDesigner from "./components/BoardDesigner.vue";
import SettingsPage from "./components/SettingsPage.vue";
import ContentRecords from "./components/ContentRecords.vue";
import DeviceConsole from "./components/DeviceConsole.vue";
import AccessGate from "./components/AccessGate.vue";
import AccountLicense from "./components/AccountLicense.vue";
import SystemAdmin from "./components/SystemAdmin.vue";
import { model, replaceModel, clone } from "./store";
import {
  api,
  ApiError,
  pendingInputs,
  premiumAvailable,
  refreshModel,
  session,
} from "./api";
import {
  addDays,
  dateKey,
  defaultLayout,
  emptyModel,
  isAdmin,
  layoutIssue,
  newDraft,
  statusLabel,
  timeLabel,
  visible,
  type Content,
  type Draft,
  type Kind,
  type Layout,
} from "./domain";

const localStorage = window.localStorage;
const siteReady = ref(false);
const site = reactive({
  initialized: false,
  registrationOpen: false,
  siteName: "班级看板",
});
async function readSite() {
  Object.assign(site, await api("/setup/status"));
  siteReady.value = true;
}
async function initialized(token: string) {
  session.token = token;
  sessionStorage.setItem("kanban-session", token);
  await run(async () => {
    await readSite();
    await refresh();
    page.value = session.systemAdmin ? "system" : "overview";
    history.replaceState(null, "", "/" + location.search);
    await handleEntry();
  });
}
async function systemSignedOut(message: string) {
  session.token = "";
  session.systemAdmin = false;
  sessionStorage.removeItem("kanban-session");
  replaceModel(emptyModel());
  page.value = "overview";
  await run(readSite);
  flash(message);
}

const page = ref("overview"),
  loading = ref(false),
  error = ref(""),
  notice = ref(""),
  now = ref(Date.now());
const adminWorkspace = computed(
  () => page.value === "system" && session.systemAdmin,
);
watch(
  [() => site.siteName, adminWorkspace],
  () => {
    document.title = `${site.siteName} · ${adminWorkspace.value ? "管理后台" : "教师工作台"}`;
  },
  { immediate: true },
);
const adminView = ref<InstanceType<typeof SystemAdmin>>();
async function refreshAdminContext() {
  // 后台弹层包含只显示一次的初始密码/许可码，刷新账号上下文不能卸载后台组件。
  try {
    await readSite();
    await refresh();
  } catch (e) {
    error.value = (e as Error).message;
  }
}
function enterTeaching(classId?: string) {
  if (classId) model.selected = classId;
  page.value = classId ? "settings" : "overview";
  history.replaceState(null, "", "/" + location.search);
}
const allNav = [
  { id: "overview", label: "班级概览", icon: LayoutDashboard },
  { id: "contents", label: "作业与提醒", icon: BookOpen },
  { id: "drafts", label: "私人草稿", icon: FilePenLine },
  { id: "appearance", label: "展示设计", icon: Palette },
  { id: "devices", label: "大屏控制", icon: Monitor },
  { id: "settings", label: "班级设置", icon: Settings },
];
const current = computed(() =>
  model.classes.find((c) => c.id === model.selected),
);
const admin = computed(
  () => isAdmin(current.value) && !current.value?.archived,
);
const premium = computed(() => premiumAvailable(now.value));
const effectiveLayout = computed(() =>
  current.value?.premiumActive ? current.value.layout : defaultLayout(),
);
const identity = computed(() =>
  session.systemAdmin
    ? "系统管理员"
    : admin.value || session.canCreateClasses
      ? "班主任"
      : "普通老师",
);
const nav = computed(() =>
  allNav
    .filter((item) =>
      current.value
        ? admin.value || ["overview", "contents", "drafts"].includes(item.id)
        : !session.systemAdmin && ["overview", "drafts"].includes(item.id),
    )
    .map((item) =>
      item.id === "overview"
        ? { ...item, label: admin.value ? "班级工作台" : "教学工作台" }
        : item,
    ),
);
watch(
  [
    () => current.value?.role,
    () => session.systemAdmin,
    () => current.value?.id,
  ],
  () => {
    if (
      page.value !== "license" &&
      !(page.value === "system" && session.systemAdmin) &&
      !nav.value.some((item) => item.id === page.value)
    )
      page.value = session.systemAdmin ? "system" : "overview";
  },
);
const today = dateKey(),
  selectedDate = ref(today);
const inClass = computed(() =>
  model.contents.filter((c) => c.classId === current.value?.id),
);
const active = computed(() =>
  inClass.value.filter((c) => visible(c, now.value)),
);
const dated = computed(() => {
  const start = new Date(`${selectedDate.value}T00:00:00+08:00`).getTime();
  const end = start + 86400000;
  // 按北京时间的半开区间查询，当日零点已结束的内容不混入下一天。
  return inClass.value.filter(
    (c) =>
      c.state === "published" &&
      new Date(c.from).getTime() < end &&
      new Date(c.until).getTime() > start,
  );
});
const drafts = computed(() => {
  const local = pendingInputs();
  const combined = model.drafts.map(
    (d) => local.find((l) => l.id === d.id) ?? d,
  );
  return [
    ...combined,
    ...local.filter((d) => !combined.some((v) => v.id === d.id)),
  ]
    .filter(
      (d) => page.value === "drafts" || d.classIds.includes(model.selected),
    )
    .sort((a, b) => b.updated.localeCompare(a.updated));
});
const devices = computed(() =>
  model.devices.filter((d) => d.classId === model.selected),
);
const editor = ref<Draft | null>(null),
  layoutDraft = ref<Layout>(defaultLayout());
const recordsView = ref<InstanceType<typeof ContentRecords>>();
const classDialog = ref<HTMLDialogElement>(),
  bindDialog = ref<HTMLDialogElement>(),
  confirmDialog = ref<HTMLDialogElement>(),
  detailsDialog = ref<HTMLDialogElement>();
const accountDialog = ref<HTMLDialogElement>(),
  incomingTransfers = ref<any[]>([]);
const joinDialog = ref<HTMLDialogElement>(),
  joinInfo = ref<any>(null),
  inviteToken = ref("");
const passwordForm = reactive({ oldPassword: "", newPassword: "" });
const classForm = reactive({ name: "", stage: "小学" }),
  pairCode = ref(new URLSearchParams(location.search).get("pair") ?? ""),
  pairName = ref("");
const detailTitle = ref(""),
  details = ref<unknown>(null),
  confirmTitle = ref(""),
  confirmBody = ref("");
let confirmAction: (() => Promise<void>) | undefined;
const dateText = computed(() =>
  new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai",
    month: "long",
    day: "numeric",
    weekday: "long",
  }).format(now.value),
);
let hydratingLayout = false;
const layoutKey = () =>
  `kanban-layout-input-${session.userId}-${model.selected}`;
function loadLayout() {
  if (!current.value) return;
  hydratingLayout = true;
  try {
    const stored = JSON.parse(localStorage.getItem(layoutKey()) ?? "null");
    layoutDraft.value =
      stored && !layoutIssue(stored) && Number.isInteger(stored.revision)
        ? stored
        : clone(current.value.layout);
    if (stored)
      flash(
        stored.revision === current.value.layout.revision
          ? "已恢复本机未发布的布局预览。"
          : "已保留旧修订的本机布局，请与正式布局比较后再发布。",
      );
  } catch {
    layoutDraft.value = clone(current.value.layout);
  } finally {
    hydratingLayout = false;
  }
}
watch(
  layoutDraft,
  () => {
    if (hydratingLayout || page.value !== "appearance" || !admin.value) return;
    try {
      localStorage.setItem(layoutKey(), JSON.stringify(layoutDraft.value));
    } catch {
      error.value = "本机无法保存布局预览，请保持此页打开。";
    }
  },
  { deep: true, flush: "sync" },
);
const previewClass = computed(() => {
  const c = current.value;
  if (!c) return undefined;
  const key = dateKey(now.value),
    weekday = new Date(`${key}T12:00:00+08:00`).getUTCDay() || 7;
  const template = c.templates?.find(
    (t) => t.effectiveFrom <= key && key <= t.effectiveUntil,
  );
  const override = c.overrides?.[key];
  const periods =
    override?.mode === "closed"
      ? []
      : override?.mode === "custom"
        ? override.periods
        : (template?.weekdays[
            String(override?.mode === "weekday" ? override.weekday : weekday)
          ] ?? []);
  const time = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Shanghai",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(now.value);
  const currentPeriod = periods.findIndex(
    (p) => p.start <= time && time < p.end,
  );
  const next = periods.find((p) => p.start > time);
  return {
    ...c,
    timetable: periods.map((p) => p.subject),
    currentPeriod,
    nextPeriod:
      currentPeriod < 0 && next ? `下一节 ${next.subject} ${next.start}` : "",
    timetableState:
      template || override?.mode === "closed" || override?.mode === "custom"
        ? "configured"
        : "pending",
  };
});
watch(
  () => current.value?.id,
  () => {
    if (current.value) {
      loadLayout();
      localStorage.setItem(`kanban-class-${session.userId}`, model.selected);
    }
  },
);
let noticeTimer: ReturnType<typeof setTimeout>;
function flash(message: string) {
  notice.value = message;
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => (notice.value = ""), 6000);
}
async function run(action: () => Promise<void>) {
  error.value = "";
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  }
}
async function refresh() {
  now.value = Date.now();
  const selected = model.selected;
  const value = await refreshModel();
  if (value.classes.some((c) => c.id === selected)) value.selected = selected;
  if (!value.classes.some((c) => c.id === value.selected))
    value.selected = value.classes[0]?.id ?? "";
  replaceModel(value);
  if (page.value === "contents") await recordsView.value?.reload();
}
async function handleEntry() {
  const invite = new URLSearchParams(location.search).get("invite");
  if (invite) {
    inviteToken.value = invite;
    joinInfo.value = await api(`/invites/${encodeURIComponent(invite)}`);
    joinDialog.value?.showModal();
  } else if (pairCode.value && current.value) bindDialog.value?.showModal();
}
async function applyToClass() {
  await run(async () => {
    const value = await api(
      `/invites/${encodeURIComponent(inviteToken.value)}/apply`,
      "POST",
    );
    joinDialog.value?.close();
    flash(value.message);
    history.replaceState(null, "", "/");
  });
}
async function logout() {
  if (pendingInputs().length) {
    ask(
      "退出前处理本机输入",
      "当前有未同步的本机草稿。确认退出将清除本账号的本机副本；已同步云草稿保留。",
      async () => {
        Object.keys(localStorage)
          .filter((k) => k.startsWith(`kanban-input-${session.userId}-`))
          .forEach((k) => localStorage.removeItem(k));
        await finishLogout();
      },
    );
    return;
  }
  await finishLogout();
}
async function finishLogout() {
  try {
    await api("/auth/logout", "POST");
  } catch {
    /* 网络中断或会话已失效时仍须允许退出本机。 */
  } finally {
    session.token = "";
    session.systemAdmin = false;
    sessionStorage.removeItem("kanban-session");
    replaceModel(emptyModel());
    page.value = "overview";
    error.value = "";
    await run(readSite);
  }
}
async function openAccount() {
  accountDialog.value?.showModal();
  await run(async () => {
    incomingTransfers.value = await api("/transfers");
  });
}
async function changePassword() {
  await run(async () => {
    await api("/auth/change-password", "POST", passwordForm);
    passwordForm.oldPassword = passwordForm.newPassword = "";
    session.token = "";
    sessionStorage.removeItem("kanban-session");
    replaceModel(emptyModel());
    accountDialog.value?.close();
    flash("密码已修改，原会话已撤销，请重新登录。");
  });
}
async function acceptTransfer(t: any) {
  await run(async () => {
    await api(`/classes/${t.classId}/transfers/${t.id}/accept`, "POST");
    await refresh();
    incomingTransfers.value = await api("/transfers");
    flash("所有权已接受，原所有者保留管理员身份。");
  });
}
async function createClass() {
  await run(async () => {
    const c = await api("/classes", "POST", classForm);
    await refresh();
    model.selected = c.id;
    classDialog.value?.close();
    classForm.name = "";
    await adminView.value?.reload();
    flash("班级已创建");
  });
}
function edit(kind: Kind, draft?: Draft, source?: Content) {
  if (draft) {
    editor.value = clone(draft);
    return;
  }
  if (!current.value || current.value.archived) return;
  if (draft) editor.value = clone(draft);
  else {
    const previousSubject =
      localStorage.getItem(
        `kanban-subject-${session.userId}-${model.selected}`,
      ) ?? "";
    const d = newDraft(
      model.selected,
      kind,
      (current.value.authorizedSubjects ?? current.value.subjects).includes(
        previousSubject,
      )
        ? previousSubject
        : (current.value.authorizedSubjects ?? current.value.subjects)[0],
    );
    if (kind === "todo" && !isAdmin(current.value))
      d.subject =
        (current.value.authorizedSubjects ?? current.value.subjects)[0] ?? "";
    if (source)
      Object.assign(d, {
        subject: source.subject,
        body: source.body,
        from: source.state === "published" ? source.from : d.from,
        until: source.state === "published" ? source.until : d.until,
        due: source.state === "published" ? source.due : "",
        sourceId: source.state === "published" ? source.id : undefined,
        baseRevision:
          source.state === "published" ? source.revision : undefined,
        priority: source.priority ?? false,
      });
    editor.value = d;
  }
}
async function published(count: number) {
  editor.value = null;
  await run(refresh);
  flash(`已发布到 ${count} 个班，设备同步与应用状态请在控制台查看。`);
}
function ask(title: string, body: string, action: () => Promise<void>) {
  confirmTitle.value = title;
  confirmBody.value = body;
  confirmAction = action;
  confirmDialog.value?.showModal();
}
async function confirmed() {
  await run(async () => {
    await confirmAction?.();
    confirmDialog.value?.close();
  });
}
async function changeContent(c: Content, action: string) {
  let reason = "";
  if (c.authorId !== session.userId) {
    reason = window.prompt("管理员代改需要填写原因（1 至 100 字）") ?? "";
    if (!reason.trim()) return;
  }
  ask(
    action === "withdraw"
      ? "撤回正式内容"
      : action === "complete"
        ? "标记提醒完成"
        : "恢复提醒",
    `${current.value?.name} · ${c.subject}。操作将更新正式版本，历史记录保留。`,
    async () => {
      let until: string | undefined;
      if (action === "restore" && new Date(c.until).getTime() <= now.value) {
        const input = window.prompt(
          "展示已结束，请输入新的结束时间，例如 2026-11-04T18:00",
        );
        if (!input) return;
        until = new Date(`${input}:00+08:00`).toISOString();
      }
      await api(`/contents/${c.id}/${action}`, "POST", {
        revision: c.revision,
        reason,
        ...(until ? { until } : {}),
      });
      await refresh();
      flash("正式记录已更新，等待设备应用。");
    },
  );
}
async function showHistory(c: Content) {
  await run(async () => {
    details.value = await api(`/contents/${c.id}/history`);
    detailTitle.value = `${c.subject} · 修改历史`;
    detailsDialog.value?.showModal();
  });
}
function publishLayout() {
  if (!current.value) return;
  const issue = layoutIssue(layoutDraft.value);
  if (issue) {
    error.value = issue;
    return;
  }
  ask(
    "发布布局与主题",
    `将更新${current.value.name}的 ${devices.value.length} 台绑定设备。离线设备恢复后同步；现场材质限制继续生效。`,
    async () => {
      await api(
        `/classes/${model.selected}/layout`,
        "PUT",
        clone(layoutDraft.value),
      );
      await refresh();
      hydratingLayout = true;
      layoutDraft.value = clone(current.value!.layout);
      hydratingLayout = false;
      try {
        localStorage.removeItem(layoutKey());
      } catch {
        /* 正式发布已确认，不因本机清理失败重复发布。 */
      }
      flash("布局已发布，设备按无打扰规则应用。");
    },
  );
}
async function inspectPair() {
  await run(async () => {
    const r = await api("/device-pairings/inspect", "POST", {
      classId: model.selected,
      code: pairCode.value,
    });
    pairName.value = r.name;
  });
}
async function bind() {
  await run(async () => {
    await api("/device-pairings/confirm", "POST", {
      classId: model.selected,
      code: pairCode.value,
    });
    await refresh();
    bindDialog.value?.close();
    pairName.value = "";
    flash("设备绑定成功，等待首次快照应用。");
  });
}
let ticker: ReturnType<typeof setInterval>;
onMounted(async () => {
  await run(readSite);
  if (session.token && site.initialized) {
    loading.value = true;
    await run(async () => {
      try {
        await refresh();
        page.value = session.systemAdmin ? "system" : "overview";
        await handleEntry();
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) {
          session.token = "";
          sessionStorage.removeItem("kanban-session");
        }
        throw e;
      }
    });
    loading.value = false;
  }
  if (current.value) loadLayout();
  ticker = setInterval(() => {
    now.value = Date.now();
  }, 5000);
});

onUnmounted(() => {
  clearInterval(ticker);
  clearTimeout(noticeTimer);
});
</script>

<template>
  <main v-if="!siteReady" class="empty-state">
    <h2>正在连接看板服务</h2>
    <p v-if="error" class="field-error">{{ error }}</p>
    <button v-if="error" class="button secondary" @click="run(readSite)">
      重新连接
    </button>
  </main>
  <AccessGate
    v-else-if="!session.token"
    :site="site"
    @authenticated="initialized"
  />
  <div v-else class="app-shell" :class="{ 'admin-mode': adminWorkspace }">
    <aside v-if="!adminWorkspace" class="sidebar">
      <div class="brand">
        <span class="brand-mark"><School :size="23" /></span
        ><span
          >{{ site.siteName }}<small>{{ identity }}工作台</small></span
        >
      </div>
      <div class="workspace-label">我的工作空间</div>
      <nav aria-label="主要导航">
        <button
          v-for="item in nav"
          :key="item.id"
          :aria-label="item.label"
          :title="item.label"
          :class="{ active: page === item.id }"
          @click="page = item.id"
        >
          <component :is="item.icon" :size="19" />{{ item.label
          }}<span
            v-if="item.id === 'drafts' && drafts.length"
            class="nav-count"
            >{{ drafts.length }}</span
          >
        </button>
        <button
          v-if="session.systemAdmin"
          :class="{ active: page === 'system' }"
          aria-label="系统管理"
          title="系统管理"
          @click="page = 'system'"
        >
          <ShieldCheck :size="19" />系统管理
        </button>
      </nav>
      <button
        class="license-nav"
        :class="{ active: page === 'license' }"
        @click="page = 'license'"
      >
        <ShieldCheck :size="18" /><span
          >我的账号<small>{{
            premium ? "高级功能可用" : "基础版 · 免费使用"
          }}</small></span
        >
      </button>
      <div class="sidebar-bottom">
        <button class="user-control" @click="logout">
          <span class="avatar">{{ session.name.slice(0, 1) }}</span
          ><span>{{ session.name }}<small>退出登录</small></span
          ><LogOut :size="17" />
        </button>
      </div>
    </aside>
    <div class="workspace">
      <header v-if="!adminWorkspace" class="topbar">
        <div
          v-if="page !== 'system' && model.classes.length"
          class="class-selector"
        >
          <School :size="19" /><select
            v-model="model.selected"
            aria-label="切换当前班级"
          >
            <option v-if="!model.classes.length" value="">我的班级</option>
            <option v-for="c in model.classes" :key="c.id" :value="c.id">
              {{ c.name }}{{ c.archived ? "（已归档）" : "" }}
            </option></select
          ><ChevronDown :size="15" />
        </div>
        <button
          class="text-button"
          v-if="session.canCreateClasses && page !== 'system'"
          aria-label="创建班级"
          @click="classDialog?.showModal()"
        >
          <Plus :size="16" /><span class="create-class-label">创建班级</span>
        </button>
        <div class="topbar-right">
          <span class="role-label"
            ><ShieldCheck :size="15" />{{ identity }}</span
          ><button
            class="icon-button"
            aria-label="刷新班级数据"
            @click="run(refresh)"
          >
            <RefreshCw :size="18" /></button
          ><button
            class="avatar small-avatar account-avatar"
            aria-label="我的账号"
            @click="openAccount"
          >
            {{ session.name.slice(0, 1) }}
          </button>
        </div>
      </header>
      <main class="main-content">
        <div v-if="error" class="notice danger" role="alert">
          <span>{{ error }}</span
          ><button
            class="icon-button"
            aria-label="关闭错误提示"
            @click="error = ''"
          >
            <X :size="16" />
          </button>
        </div>
        <div v-if="current?.archived && !adminWorkspace" class="notice warning">
          班级已归档，历史只读。恢复后需重新绑定设备。<button
            v-if="current.role === 'owner'"
            class="text-button"
            @click="
              run(async () => {
                await api(`/classes/${model.selected}/restore`, 'POST');
                await refresh();
              })
            "
          >
            恢复班级
          </button>
        </div>
        <SystemAdmin
          v-if="adminWorkspace"
          ref="adminView"
          @create-class="classDialog?.showModal()"
          @changed="refreshAdminContext"
          @restored="systemSignedOut"
          @teaching="enterTeaching()"
          @open-class="enterTeaching"
          @logout="logout"
        />
        <div v-else-if="loading" class="empty-state">
          <RefreshCw :size="30" />
          <h2>正在读取班级信息</h2>
        </div>
        <AccountLicense
          v-else-if="page === 'license'"
          @changed="run(refresh)"
        />
        <section v-else-if="!current && page === 'drafts'" class="panel">
          <h1>私人草稿</h1>
          <p class="notice subtle">
            草稿正文仍归你保存。失去目标班级权限后无法发布，可继续编辑本人草稿。
          </p>
          <p v-if="!drafts.length" class="empty-state">暂无私人草稿。</p>
          <article v-for="d in drafts" :key="d.id" class="record-row">
            <h3>{{ d.subject }}</h3>
            <p class="record-body">{{ d.body }}</p>
            <button class="text-button" @click="edit(d.kind, d)">
              继续编辑本人草稿
            </button>
          </article>
        </section>
        <section
          v-else-if="!current && !session.canCreateClasses"
          class="first-run"
        >
          <span class="large-icon"><School :size="36" /></span>
          <h1>等待分配任教班级</h1>
          <p>请联系管理员分配班级和学科。</p>
          <button class="button secondary" @click="run(refresh)">
            <RefreshCw :size="18" />刷新我的班级
          </button>
        </section>
        <section v-else-if="!current" class="first-run">
          <span class="large-icon"><School :size="36" /></span>
          <h1>创建班级</h1>
          <button class="button primary" @click="classDialog?.showModal()">
            <Plus :size="18" />创建第一个班级
          </button>
        </section>
        <template v-else>
          <div class="page-heading">
            <div>
              <p class="eyebrow">
                {{ dateText }}<span class="heading-dot">·</span
                >{{ current.name }}
              </p>
              <h1>{{ nav.find((n) => n.id === page)?.label }}</h1>
            </div>
            <div
              class="heading-actions"
              v-if="page === 'overview' || page === 'contents'"
            >
              <button
                v-if="admin"
                class="button secondary"
                :disabled="current.archived"
                @click="edit('todo')"
              >
                <Bell :size="17" />发布提醒</button
              ><button
                class="button primary"
                :disabled="current.archived"
                @click="edit('homework')"
              >
                <Plus :size="18" />发布作业
              </button>
            </div>
          </div>
          <template v-if="page === 'overview'">
            <div v-if="admin" class="sync-strip">
              <span
                class="status-dot"
                :class="{ amber: !devices.some((d) => d.online) }"
              ></span
              ><strong>{{
                devices.length
                  ? `${devices.filter((d) => d.online).length} 台设备在线`
                  : "尚未绑定教室设备"
              }}</strong
              ><button class="text-button" @click="page = 'devices'">
                {{ devices.length ? "控制大屏" : "前往绑定"
                }}<ArrowUpRight :size="15" />
              </button>
            </div>
            <div class="overview-grid">
              <div>
                <section class="panel content-panel">
                  <div class="section-heading">
                    <h2>
                      <BookOpen :size="20" />今日作业<span class="count-pill">{{
                        dated.filter((c) => c.kind === "homework").length
                      }}</span>
                    </h2>
                    <div class="date-control">
                      <button
                        class="icon-button"
                        aria-label="查看前一天"
                        @click="selectedDate = addDays(selectedDate, -1)"
                      >
                        <ChevronLeft :size="16" /></button
                      ><input
                        type="date"
                        v-model="selectedDate"
                        aria-label="查看作业日期"
                      /><button
                        class="icon-button"
                        aria-label="查看后一天"
                        @click="selectedDate = addDays(selectedDate, 1)"
                      >
                        <ChevronRight :size="16" />
                      </button>
                    </div>
                  </div>
                  <div
                    v-if="selectedDate !== today"
                    class="notice subtle small"
                  >
                    只切换查看日期，不改变大屏日期。<button
                      class="text-button"
                      @click="selectedDate = today"
                    >
                      返回今天
                    </button>
                  </div>
                  <div
                    v-if="!dated.some((c) => c.kind === 'homework')"
                    class="empty-state compact"
                  >
                    <BookOpen :size="28" />
                    <h3>还没有作业</h3>
                    <button
                      class="text-button"
                      :disabled="current.archived"
                      @click="edit('homework')"
                    >
                      发布第一条作业<ArrowRight :size="15" />
                    </button>
                  </div>
                  <div
                    class="bounded-content-list"
                    role="region"
                    aria-label="今日作业列表，可滚动查看"
                    tabindex="0"
                  >
                    <article
                      v-for="c in dated.filter((c) => c.kind === 'homework')"
                      :key="c.id"
                      class="homework-row"
                    >
                      <div
                        class="subject-icon"
                        :class="{
                          blue: c.subject === '数学',
                          gold: c.subject === '英语',
                        }"
                      >
                        {{ c.subject.slice(0, 1) }}
                      </div>
                      <div class="homework-body">
                        <div class="row-heading">
                          <h3>{{ c.subject }}</h3>
                          <span class="record-meta"
                            >{{ c.author }} · {{ timeLabel(c.created) }}</span
                          >
                        </div>
                        <p>{{ c.body }}</p>
                        <div class="row-footer">
                          <span
                            >{{ statusLabel(c, now) }} · 展示至
                            {{ timeLabel(c.until, true) }}</span
                          ><button
                            class="text-button"
                            :disabled="
                              current.archived ||
                              (!admin && c.authorId !== session.userId)
                            "
                            @click="edit('homework', undefined, c)"
                          >
                            编辑<ArrowUpRight :size="13" />
                          </button>
                        </div>
                      </div>
                    </article>
                  </div>
                </section>
                <section class="panel reminders-panel">
                  <div class="section-heading">
                    <h2>
                      <Bell :size="19" />班级提醒<span class="count-pill">{{
                        dated.filter((c) => c.kind === "todo").length
                      }}</span>
                    </h2>
                    <button
                      v-if="admin"
                      class="text-button"
                      :disabled="current.archived"
                      @click="edit('todo')"
                    >
                      <Plus :size="16" />添加
                    </button>
                  </div>
                  <p
                    v-if="!dated.some((c) => c.kind === 'todo')"
                    class="inline-empty"
                  >
                    暂无提醒
                  </p>
                  <div
                    class="bounded-content-list reminder-list"
                    role="region"
                    aria-label="班级提醒列表，可滚动查看"
                    tabindex="0"
                  >
                    <article
                      v-for="c in dated.filter((c) => c.kind === 'todo')"
                      :key="c.id"
                      class="todo-row"
                    >
                      <button
                        class="todo-check"
                        aria-label="标记提醒完成"
                        :disabled="
                          current.archived ||
                          (!admin && c.authorId !== session.userId)
                        "
                        @click="changeContent(c, 'complete')"
                      >
                        <Check :size="14" />
                      </button>
                      <div>
                        <p>{{ c.body }}</p>
                        <small>{{ c.subject }} · {{ c.author }}</small>
                      </div>
                      <span
                        v-if="c.due && new Date(c.due).getTime() < now"
                        class="badge amber-badge"
                        >已逾期</span
                      >
                    </article>
                  </div>
                </section>
              </div>
              <div class="overview-side">
                <section class="panel preview-card">
                  <div class="section-heading">
                    <h2><Monitor :size="19" />大屏预览</h2>
                    <span class="badge neutral">预览</span>
                  </div>
                  <BoardPreview
                    v-if="previewClass"
                    :classroom="previewClass"
                    :contents="active"
                    :layout="effectiveLayout"
                    :now="now"
                  />
                  <div class="preview-card-footer">
                    <span
                      >{{
                        current.layout.theme === "mica_light"
                          ? "云母浅灰"
                          : "云母深灰"
                      }}
                      ·
                      {{
                        current.layout.material === "subtle"
                          ? "柔和材质"
                          : "纯色"
                      }}</span
                    ><button
                      v-if="admin"
                      class="text-button"
                      @click="page = 'appearance'"
                    >
                      调整展示<ArrowUpRight :size="14" />
                    </button>
                  </div>
                </section>
                <section class="panel draft-card">
                  <div class="section-heading">
                    <h2><FilePenLine :size="19" />继续草稿</h2>
                    <span class="count-pill">{{ drafts.length }}</span>
                  </div>
                  <p v-if="!drafts.length" class="inline-empty">暂无草稿</p>
                  <button
                    v-for="d in drafts.slice(0, 2)"
                    :key="d.id"
                    class="draft-link"
                    @click="edit(d.kind, d)"
                  >
                    <span class="draft-mini-icon"
                      ><FilePenLine :size="19" /></span
                    ><span
                      ><strong
                        >{{ d.subject
                        }}{{ d.kind === "homework" ? "作业" : "提醒" }}</strong
                      ><small
                        >{{ timeLabel(d.updated, true) }} ·
                        {{ d.revision ? "可继续编辑" : "本机未同步" }}</small
                      ></span
                    ><ArrowRight :size="17" />
                  </button>
                </section>
                <div v-if="current.message" class="daily-note">
                  <span>班级寄语</span>
                  <p>
                    {{ current.message }}
                  </p>
                  <span class="note-decoration" aria-hidden="true">✳</span>
                </div>
              </div>
            </div>
          </template>
          <ContentRecords
            v-else-if="page === 'contents'"
            ref="recordsView"
            :classroom="current"
            :now="now"
            @edit="edit($event.kind, undefined, $event)"
            @history="showHistory"
            @action="changeContent"
          />
          <section v-else-if="page === 'drafts'" class="panel">
            <div v-if="!drafts.length" class="empty-state">
              <FilePenLine :size="34" />
              <h2>还没有草稿</h2>
            </div>
            <article v-for="d in drafts" :key="d.id" class="record-row">
              <div class="record-top">
                <span class="subject-tag">{{ d.subject }}</span
                ><span class="badge neutral">私人草稿</span
                ><small>{{ timeLabel(d.updated, true) }}</small>
              </div>
              <p class="record-body">{{ d.body || "尚未填写正文" }}</p>
              <p class="record-meta">
                {{
                  model.classes
                    .filter((c) => d.classIds.includes(c.id))
                    .map((c) => c.name)
                    .join("、")
                }}
                · {{ d.sourceId ? "正式内容的修改草稿" : "新建内容" }}
              </p>
              <div class="record-actions">
                <button class="text-button" @click="edit(d.kind, d)">
                  继续编辑<ArrowRight :size="15" /></button
                ><button
                  class="text-button destructive"
                  @click="
                    ask(
                      '放弃私人草稿',
                      '本次操作不会撤回已经发布的正式内容。',
                      async () => {
                        await api(`/drafts/${d.id}`, 'DELETE');
                        localStorage.removeItem(
                          `kanban-input-${session.userId}-${d.id}`,
                        );
                        await refresh();
                      },
                    )
                  "
                >
                  放弃草稿
                </button>
              </div>
            </article>
          </section>
          <BoardDesigner
            v-else-if="page === 'appearance' && admin && previewClass"
            v-model="layoutDraft"
            :classroom="previewClass"
            :contents="active"
            :now="now"
            :disabled="!admin || !premium"
            :publish-error="error"
            :publish-notice="notice"
            @publish="publishLayout"
          />
          <DeviceConsole
            v-else-if="page === 'devices' && admin"
            :classroom="current"
            :devices="devices"
            :now="now"
            @refresh="run(refresh)"
            @bind="
              pairName = '';
              bindDialog?.showModal();
            "
            @design="page = 'appearance'"
          />
          <template v-else-if="page === 'settings' && admin"
            ><SettingsPage :classroom="current" @refresh="run(refresh)"
          /></template>
        </template>
        <footer v-if="!adminWorkspace" class="workspace-footer">
          <span>班级看板</span>
        </footer>
      </main>
    </div>
    <nav v-if="!adminWorkspace" class="mobile-nav" aria-label="手机底部导航">
      <button
        :class="{
          active: ['overview', 'appearance', 'devices'].includes(page),
        }"
        @click="page = 'overview'"
      >
        <School :size="21" />班级</button
      ><button
        :class="{ active: ['contents', 'drafts'].includes(page) }"
        @click="page = 'contents'"
      >
        <BookOpen :size="21" />记录</button
      ><button :class="{ active: page === 'settings' }" @click="openAccount">
        <Users :size="21" />我的
      </button>
    </nav>
  </div>
  <div v-if="notice" class="toast" role="status">
    <Check :size="18" />{{ notice }}
  </div>
  <EditorSheet
    v-if="editor"
    :key="editor.id"
    :draft="editor"
    @close="
      editor = null;
      run(refresh);
    "
    @published="published"
  />
  <dialog ref="joinDialog" class="modal">
    <header class="section-heading">
      <h2>申请加入班级</h2>
      <button
        class="icon-button"
        aria-label="关闭入班申请"
        @click="joinDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <template v-if="joinInfo"
      ><h3>{{ joinInfo.className }}</h3>
      <p class="muted">授权学科：{{ joinInfo.subjects.join("、") }}</p>
      <p class="notice subtle">
        以 {{ session.name }} 的账号申请，需班级管理员核对后生效。
      </p>
      <p v-if="error" class="field-error">{{ error }}</p>
      <button class="button primary full" @click="applyToClass">
        {{
          joinInfo.alreadyApplied
            ? "查看申请状态：等待管理员审批"
            : "申请加入此班级"
        }}
      </button></template
    >
  </dialog>
  <dialog ref="accountDialog" class="modal">
    <header class="section-heading">
      <h2>{{ session.name }} · 我的账号</h2>
      <button
        class="icon-button"
        aria-label="关闭账号设置"
        @click="accountDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <p class="muted">账号：{{ session.login }} · {{ identity }}</p>
    <button
      class="button secondary"
      @click="
        page = 'license';
        accountDialog?.close();
      "
    >
      账号与高级功能
    </button>
    <div class="account-shortcuts">
      <button
        v-if="session.systemAdmin"
        class="button secondary"
        @click="
          page = 'system';
          accountDialog?.close();
        "
      >
        系统管理
      </button>
      <button
        class="button secondary"
        v-if="admin"
        @click="
          page = 'settings';
          accountDialog?.close();
        "
      >
        班级设置</button
      ><button
        class="button secondary"
        @click="
          page = 'drafts';
          accountDialog?.close();
        "
      >
        私人草稿</button
      ><button
        class="button secondary"
        v-if="admin"
        @click="
          page = 'devices';
          accountDialog?.close();
        "
      >
        大屏控制
      </button>
    </div>
    <article v-for="t in incomingTransfers" :key="t.id" class="record-row">
      <p>{{ t.className }}邀请你接任所有者</p>
      <button
        class="button secondary"
        @click="
          ask(
            '接受班级所有权',
            `接受${t.className}后，你将成为该班唯一所有者。原所有者保留管理员身份。`,
            async () => {
              await acceptTransfer(t);
            },
          )
        "
      >
        查看并接受交接
      </button>
    </article>
    <form @submit.prevent="changePassword">
      <h3>修改密码</h3>
      <label class="field"
        >原密码<input
          v-model="passwordForm.oldPassword"
          type="password"
          required
          autocomplete="current-password" /></label
      ><label class="field"
        >新密码<input
          v-model="passwordForm.newPassword"
          type="password"
          required
          minlength="10"
          maxlength="128"
          autocomplete="new-password"
      /></label>
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <button class="button primary full">修改密码并重新登录</button>
    </form>
    <button
      class="text-button destructive"
      @click="
        accountDialog?.close();
        logout();
      "
    >
      退出登录
    </button>
  </dialog>
  <dialog ref="classDialog" class="modal">
    <form @submit.prevent="createClass">
      <header class="section-heading">
        <h2>创建班级</h2>
        <button
          type="button"
          class="icon-button"
          aria-label="关闭"
          @click="classDialog?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <label class="field"
        >班级名称<input
          v-model="classForm.name"
          maxlength="60"
          required
          placeholder="例如五年级二班" /></label
      ><label class="field"
        >学段<select v-model="classForm.stage">
          <option>小学</option>
          <option>初中</option>
          <option>高中</option>
        </select></label
      >
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <button class="button primary full">创建班级</button>
    </form>
  </dialog>
  <dialog ref="bindDialog" class="modal">
    <header class="section-heading">
      <h2>绑定教室电脑</h2>
      <button
        class="icon-button"
        aria-label="关闭"
        @click="bindDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <p class="muted">请在教室客户端取得绑定码，再核对设备名称与班级。</p>
    <label class="field"
      >6 位绑定码<input
        v-model="pairCode"
        inputmode="numeric"
        pattern="[0-9]{6}"
        maxlength="6"
        @input="pairName = ''"
    /></label>
    <p v-if="pairName" class="notice subtle">
      设备：{{ pairName }}<br />班级：{{ current?.name }}
    </p>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <button
      class="button primary full"
      :disabled="pairCode.length !== 6"
      @click="pairName ? bind() : inspectPair()"
    >
      {{ pairName ? "确认绑定此设备与班级" : "核对设备" }}
    </button>
  </dialog>
  <dialog ref="confirmDialog" class="modal">
    <h2>{{ confirmTitle }}</h2>
    <p>{{ confirmBody }}</p>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <div class="modal-actions">
      <button class="button secondary" @click="confirmDialog?.close()">
        取消</button
      ><button class="button primary" @click="confirmed">确认</button>
    </div>
  </dialog>
  <dialog ref="detailsDialog" class="modal wide-modal">
    <header class="section-heading">
      <h2>{{ detailTitle }}</h2>
      <button
        class="icon-button"
        aria-label="关闭"
        @click="detailsDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <article
      v-for="(entry, index) in details as any[]"
      :key="index"
      class="record-row"
    >
      <strong>修订 {{ entry.revision }} · {{ entry.editor }}</strong>
      <p>{{ entry.at }}</p>
      <p v-if="entry.reason">代改原因：{{ entry.reason }}</p>
      <p class="record-body">{{ entry.after.body }}</p>
    </article>
  </dialog>
</template>
