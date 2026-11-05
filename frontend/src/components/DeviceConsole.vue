<script setup lang="ts">
import ClassroomDisplay from "./ClassroomDisplay.vue";
import ScreenNoticeControls, {
  type ScreenNoticeInput,
} from "./ScreenNoticeControls.vue";
import { computed, ref, watch } from "vue";
import {
  ChevronLeft,
  ChevronRight,
  Laptop,
  Monitor,
  Pause,
  Play,
  Plus,
  RefreshCw,
  X,
  Pencil,
  CircleHelp,
} from "@lucide/vue";
import { deviceStatus } from "../deviceStatus";
import { api, ApiError, premiumAvailable, session } from "../api";
import { model } from "../store";
import {
  isAdmin,
  timeLabel,
  uid,
  type Classroom,
  type Command,
  type Device,
} from "../domain";

const props = defineProps<{
  classroom: Classroom;
  devices: Device[];
  now: number;
  reload: () => Promise<void>;
  focusDeviceId?: string;
  initialView?: "screen" | "notice";
}>();
const emit = defineEmits<{ refresh: []; bind: []; design: [] }>();
const selected = ref(""),
  device = computed(
    () =>
      props.devices.find((d) => d.id === selected.value) ?? props.devices[0],
  );
const deviceView = ref("screen");
const selectionKey = () =>
  `classglow-device-${session.userId}-${props.classroom.id}`;
function chooseDevice(id: string) {
  selected.value = id;
  error.value = "";
  notice.value = "";
  try {
    localStorage.setItem(selectionKey(), id);
  } catch {
    /* 当前选择仍可使用。 */
  }
}
const guideOpen = ref(false),
  renameDialog = ref<HTMLDialogElement>(),
  renameName = ref(""),
  renameTarget = ref("");
function closeGuide() {
  guideOpen.value = false;
  try {
    localStorage.setItem(`classglow-device-guide-${session.userId}`, "seen");
  } catch {
    /* 可随时重看。 */
  }
}
function rename() {
  if (!device.value) return;
  renameTarget.value = device.value.id;
  renameName.value = device.value.name;
  renameDialog.value?.showModal();
}
async function saveName() {
  await run(async () => {
    const updated = await api<Device>(
      `/devices/${renameTarget.value}`,
      "PATCH",
      { name: renameName.value.trim() },
    );
    const index = model.devices.findIndex((d) => d.id === updated.id);
    if (index >= 0) model.devices[index] = updated;
    renameDialog.value?.close();
    notice.value = `已改名为「${updated.name}」`;
  });
}
const allowed = computed(
  () => isAdmin(props.classroom) && !props.classroom.archived,
);
watch(
  [() => props.initialView, allowed],
  ([view, canManage]) => {
    deviceView.value = canManage && view === "notice" ? "notice" : "screen";
  },
  { immediate: true },
);
const error = ref(""),
  notice = ref(""),
  busy = ref(false),
  confirmation = ref<HTMLDialogElement>();
type Pending = {
  key: string;
  deviceId: string;
  payload: {
    action: string;
    expectedControlRevision: number;
    pageSetId: string;
    page: number;
    notice?: ScreenNoticeInput;
  };
};
const pending = ref<Pending | null>(null),
  confirmText = ref("");
let confirmationAction: (() => Promise<void>) | undefined;
const storageKey = () =>
  `kanban-control-${session.userId}-${props.classroom.id}`;
const actions: Record<string, string> = {
  sync: "请求同步",
  pause: "暂停看板",
  resume: "恢复看板",
  set_page: "临时翻页",
  auto_page: "恢复自动翻页",
  notice: "播放醒目提醒",
  stop_notice: "停止醒目提醒",
};
const states: Record<string, string> = {
  accepted: "等待执行",
  executed: "已执行",
  deferred: "保护延后",
  rejected: "已拒绝",
  expired: "已过期",
  superseded: "已被替代",
};
const history = computed(() =>
  model.commands
    .filter((c) => c.deviceId === device.value?.id)
    .sort((a, b) => b.time.localeCompare(a.time)),
);
const receiptId = ref("");
const receipt = computed(() =>
  model.commands.find(
    (c) => c.id === receiptId.value && c.deviceId === device.value?.id,
  ),
);
const receiptText = computed(() =>
  receipt.value
    ? `${actions[receipt.value.action]}：${receipt.value.state === "executed" && receipt.value.action === "notice" ? "已开始播放" : states[receipt.value.state]}。${receipt.value.state === "deferred" || receipt.value.state === "rejected" ? receipt.value.detail : ""}`
    : notice.value,
);
const frozen = computed(
  () =>
    !allowed.value ||
    !premiumAvailable(props.now) ||
    !device.value?.online ||
    !!pending.value ||
    busy.value,
);
const syncDisabled = computed(
  () =>
    !allowed.value || !device.value?.online || !!pending.value || busy.value,
);
const paused = computed(
  () => !!device.value?.localPaused || !!device.value?.remotePaused,
);
function remember(value: Pending | null) {
  pending.value = value;
  try {
    if (value) localStorage.setItem(storageKey(), JSON.stringify(value));
    else localStorage.removeItem(storageKey());
  } catch {
    error.value = "本机无法保存控制请求标识，请保持此页打开并先查询本次结果。";
  }
}
function restorePending() {
  try {
    const p = JSON.parse(localStorage.getItem(storageKey()) ?? "null");
    pending.value = p?.key && p?.deviceId && p?.payload ? p : null;
  } catch {
    pending.value = null;
  }
}
async function run(action: () => Promise<void>) {
  error.value = "";
  notice.value = "";
  receiptId.value = "";
  busy.value = true;
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function status() {
  if (!device.value || document.visibilityState !== "visible") return;
  const result = await api(`/devices/${device.value.id}/control`);
  const index = model.devices.findIndex((d) => d.id === result.device.id);
  if (index >= 0) model.devices[index] = result.device;
  model.commands = [
    ...model.commands.filter((c) => c.deviceId !== result.device.id),
    ...result.commands,
  ];
}
function acceptReceipt(command: Command) {
  receiptId.value = command.id;
  model.commands = [
    command,
    ...model.commands.filter((c) => c.id !== command.id),
  ];
  remember(null);
  notice.value = `${actions[command.action]}：${states[command.state] ?? "结果待确认"}。`;
  emit("refresh");
}
async function submitPending() {
  if (!pending.value) return;
  const p = pending.value;
  try {
    localStorage.setItem(storageKey(), JSON.stringify(p));
  } catch {
    throw new Error(
      "无法持久保存本次请求标识，已停止提交。请保持此页打开，先查询原请求结果。",
    );
  }
  try {
    const command = await api(
      `/devices/${p.deviceId}/commands`,
      "POST",
      p.payload,
      p.key,
    );
    acceptReceipt(command);
  } catch (e) {
    // 不确定的网络失败保留原幂等键；修订冲突表示原请求没有被本次受理，需重新确认目标。
    if (e instanceof ApiError && e.code === "control_revision_conflict") {
      remember(null);
      await status();
    }
    throw e;
  }
}
async function queryPending() {
  if (!pending.value) return;
  const command = await api(
    `/devices/${pending.value.deviceId}/commands/by-key/${pending.value.key}`,
  );
  acceptReceipt(command);
}
function ask(text: string, action: () => Promise<void>) {
  confirmText.value = text;
  confirmationAction = action;
  confirmation.value?.showModal();
}
async function confirmed() {
  await run(async () => {
    await confirmationAction?.();
    confirmation.value?.close();
  });
}
async function control(
  action: string,
  page = 1,
  screenNotice?: ScreenNoticeInput,
) {
  if (
    !device.value ||
    (["sync", "stop_notice"].includes(action)
      ? syncDisabled.value
      : frozen.value)
  )
    return;
  const d = device.value;
  const request: Pending = {
    key: uid(),
    deviceId: d.id,
    payload: {
      action,
      expectedControlRevision: d.revision,
      pageSetId: d.pageSetId ?? "",
      page,
      ...(screenNotice ? { notice: screenNotice } : {}),
    },
  };
  const execute = async () => {
    remember(request);
    await submitPending();
  };
  if (action === "pause" || action === "resume")
    ask(
      `${props.classroom.name} · ${d.name}。${action === "pause" ? "成功后恢复原壁纸，程序继续同步。" : "只解除远程暂停，本机暂停需现场恢复；恢复可能因前台保护延后。"}`,
      execute,
    );
  else await run(execute);
}
function revoke() {
  if (!device.value) return;
  const d = device.value;
  ask(
    `撤销${d.name}。旧设备下次联网后停止使用并清除班级缓存，持续离线时无法立即清屏。`,
    async () => {
      await api(`/devices/${d.id}/revoke`, "POST");
      emit("refresh");
    },
  );
}
const theme = (value: string) =>
  value === "mica_dark"
    ? "云母墨绿"
    : value === "system_high_contrast"
      ? "系统高对比"
      : "云母浅灰";
const needsCanvasUpdate = computed(() => {
  if (props.classroom.layout.editor !== "canvas") return false;
  const version = device.value?.clientVersion?.match(/^(\d+)\.(\d+)\.(\d+)$/);
  return !version || (Number(version[1]) === 0 && Number(version[2]) < 4);
});
const summary = computed(() => {
  const d = device.value;
  if (!d) return "";
  if (!d.online) return "设备暂时不可达";
  if (d.localPaused) return d.deferred ? "暂停待生效" : "本机已暂停";
  if (d.remotePaused) return "远程已暂停";
  if (d.deferred && !d.error) return "已接收，等待全屏展示结束";
  if (d.error) return "设备需要检查";
  if (needsCanvasUpdate.value) return "基础看板运行中，等待客户端升级";
  return deviceStatus(d, props.classroom.version);
});
const needsSyncUpdate = computed(() => {
  const version = device.value?.clientVersion?.split(".").map(Number);
  return (
    version?.length === 3 &&
    version[0] === 0 &&
    (version[1]! < 4 || (version[1] === 4 && version[2]! < 12))
  );
});
watch(
  [() => props.classroom.id, () => props.focusDeviceId],
  () => {
    selected.value = props.devices.some((d) => d.id === props.focusDeviceId)
      ? props.focusDeviceId!
      : "";
    try {
      if (!selected.value)
        selected.value = localStorage.getItem(selectionKey()) ?? "";
      guideOpen.value = !localStorage.getItem(
        `classglow-device-guide-${session.userId}`,
      );
    } catch {
      guideOpen.value = true;
    }
    renameDialog.value?.close();
    restorePending();
  },
  { immediate: true },
);
watch(
  () => device.value?.id,
  () => {
    error.value = "";
    notice.value = "";
    void status().catch(() => {});
  },
);
</script>

<template>
  <section class="device-console">
    <div class="section-heading device-page-heading">
      <div>
        <h2>{{ classroom.name }}的电脑</h2>
        <p class="muted">
          已绑定 {{ devices.length }} 台 · 选择一台查看画面或操作
        </p>
      </div>
      <div class="device-heading-actions">
        <button class="button secondary" @click="guideOpen = !guideOpen">
          <CircleHelp :size="17" />操作指引
        </button>
        <button class="button primary" v-if="allowed" @click="emit('bind')">
          <Plus :size="17" />绑定电脑
        </button>
      </div>
    </div>
    <section
      v-if="guideOpen"
      class="panel device-guide"
      aria-label="教室电脑操作指引"
    >
      <header>
        <h3>第一次连接，按这三步来</h3>
        <button class="text-button" @click="closeGuide">收起指引</button>
      </header>
      <ol>
        <li>
          <span>1</span>
          <div>
            <strong>在教室电脑生成绑定码</strong>
            <p>
              下载客户端，打开后生成 6 位绑定码。手机和办公电脑使用网页即可。
            </p>
            <a href="/download" target="_blank" rel="noopener">下载客户端</a>
          </div>
        </li>
        <li>
          <span>2</span>
          <div>
            <strong>在这里绑定并给电脑命名</strong>
            <p>班主任输入绑定码，命名为“讲台电脑”等。每台教室电脑分别绑定。</p>
            <button v-if="allowed" class="text-button" @click="emit('bind')">
              输入绑定码
            </button>
          </div>
        </li>
        <li>
          <span>3</span>
          <div>
            <strong>回到教室电脑启用展示</strong>
            <p>
              勾选“启用桌面展示”。以后网页发布作业会自动同步，客户端收进托盘后继续运行。
            </p>
          </div>
        </li>
      </ol>
    </section>
    <div v-if="error" class="notice danger" role="alert">{{ error }}</div>
    <div v-if="receiptText" class="notice subtle" role="status">
      {{ receiptText }}
    </div>
    <div v-if="pending" class="notice warning control-pending">
      <span>上次操作结果待确认</span
      ><button class="text-button" :disabled="busy" @click="run(queryPending)">
        查询原请求结果</button
      ><button class="text-button" :disabled="busy" @click="run(submitPending)">
        重试
      </button>
    </div>
    <p v-if="allowed && !premiumAvailable(now)" class="notice subtle">
      远程控制需高级许可。
    </p>
    <div class="device-picker" aria-label="选择教室电脑">
      <button
        v-for="d in devices"
        :key="d.id"
        :class="{ selected: device?.id === d.id }"
        :aria-pressed="device?.id === d.id"
        @click="chooseDevice(d.id)"
      >
        <span class="device-picker-title"
          ><Laptop :size="18" /><strong>{{ d.name }}</strong
          ><span class="status-dot" :class="{ amber: !d.online }"></span
        ></span>
        <span class="device-picker-state">{{
          deviceStatus(d, classroom.version)
        }}</span>
        <span class="device-picker-meta"
          >{{ d.online ? "在线" : "离线" }} · 编号 {{ d.id.slice(0, 6)
          }}<b v-if="device?.id === d.id">当前选择</b></span
        >
      </button>
    </div>
    <div v-if="device" class="device-target">
      <div>
        <span>当前电脑</span><strong>{{ device.name }}</strong
        ><button v-if="allowed" class="text-button" @click="rename">
          <Pencil :size="15" />改名
        </button>
      </div>
      <p v-if="deviceView === 'notice'">
        提醒只在这台电脑播放。可在上方切换目标。
      </p>
      <p v-else>
        发布内容会同步到本班全部电脑；下方查看画面、暂停和翻页只针对这台。
      </p>
    </div>
    <p v-if="needsSyncUpdate" class="notice subtle">
      这台电脑仍使用旧版定时同步。<a
        href="/download"
        target="_blank"
        rel="noopener"
        >升级到 0.4.12</a
      >
      后可及时接收更新，覆盖安装保留绑定。
    </p>
    <nav v-if="device" class="device-view-tabs" aria-label="电脑操作分类">
      <button
        v-for="tab in [
          { id: 'screen', name: '当前画面' },
          ...(allowed
            ? [
                { id: 'notice', name: '醒目提醒' },
                { id: 'history', name: '操作记录' },
              ]
            : []),
        ]"
        :key="tab.id"
        :aria-pressed="deviceView === tab.id"
        @click="deviceView = tab.id"
      >
        {{ tab.name }}
      </button>
    </nav>
    <ClassroomDisplay
      v-if="device && deviceView === 'screen'"
      :key="device.id"
      :device="device"
      :reload="reload"
    />
    <ScreenNoticeControls
      v-if="device && allowed && deviceView === 'notice'"
      :key="device.id + '-notice'"
      :device="device"
      :now="now"
      :disabled="
        frozen || needsSyncUpdate || device.localPaused || device.remotePaused
      "
      :stop-disabled="syncDisabled || needsSyncUpdate"
      @send="control('notice', 1, $event)"
      @stop="control('stop_notice')"
    />
    <section v-if="!device" class="panel empty-state">
      <Monitor :size="36" />
      <h2>尚未绑定教室电脑</h2>
      <p v-if="!allowed" class="muted">请联系班主任绑定教室电脑。</p>
      <p v-else-if="deviceView === 'notice'" class="muted">
        绑定并启用教室电脑后，即可向它发送醒目提醒。
      </p>
      <ol v-if="allowed" class="connect-guide">
        <li>
          <span>1</span>
          <div>
            <strong>在教室电脑安装客户端</strong
            ><a href="/download" target="_blank" rel="noopener"
              >下载 Windows 客户端</a
            >
          </div>
        </li>
        <li>
          <span>2</span>
          <div>
            <strong>打开客户端，生成绑定码</strong
            ><small>首次打开需阅读隐私说明。</small>
          </div>
        </li>
        <li>
          <span>3</span>
          <div>
            <strong>回到这里输入 6 位绑定码</strong
            ><small>核对电脑名称和班级，确认绑定。</small>
          </div>
        </li>
      </ol>
      <button v-if="allowed" class="button primary" @click="emit('bind')">
        输入绑定码
      </button>
    </section>
    <div
      v-if="device && allowed && deviceView !== 'notice'"
      class="device-grid device-view-panel"
    >
      <section v-if="deviceView === 'screen'" class="panel">
        <div class="section-heading">
          <h2><Monitor :size="20" />{{ device.name }}</h2>
          <span class="badge" :class="device.online ? '' : 'amber-badge'">{{
            device.online ? "在线" : "离线"
          }}</span>
        </div>
        <div class="device-state-banner" :class="{ paused }">
          <span
            class="status-dot"
            :class="{ amber: paused || !device.online || device.deferred }"
          ></span>
          <div>
            <h3>{{ summary }}</h3>
            <p>
              {{
                device.localPaused
                  ? "需现场老师在托盘恢复，手机不能解除。"
                  : device.online
                    ? "普通窗口下自动更新；全屏课件结束后再应用。"
                    : "内容和布局仍可发布，设备恢复联网后同步。"
              }}
            </p>
          </div>
        </div>
        <details class="device-details">
          <summary>设备详情</summary>
          <dl class="device-facts">
            <div>
              <dt>Windows 客户端</dt>
              <dd>{{ device.clientVersion || "尚未上报版本" }}</dd>
            </div>
            <div>
              <dt>班级正式版本</dt>
              <dd>v{{ classroom.version }}</dd>
            </div>
            <div>
              <dt>设备缓存版本</dt>
              <dd>v{{ device.cached }}</dd>
            </div>
            <div>
              <dt>设备应用版本</dt>
              <dd>v{{ device.applied }}</dd>
            </div>
            <div>
              <dt>最近联系</dt>
              <dd>{{ timeLabel(device.reported, true) }}</dd>
            </div>
            <div>
              <dt>班级目标样式</dt>
              <dd>
                {{ theme(classroom.layout.theme) }} ·
                {{ classroom.layout.material === "solid" ? "纯色" : "柔和" }}
              </dd>
            </div>
            <div>
              <dt>最近应用的看板样式</dt>
              <dd>
                {{
                  device.applied
                    ? `${theme(device.effectiveTheme)} · ${device.effectiveMaterial === "solid" ? "纯色" : "柔和"}`
                    : "尚未应用"
                }}
              </dd>
            </div>
          </dl>
        </details>
        <p v-if="needsCanvasUpdate" class="notice warning">
          自由画布需要 Windows 0.4.0
          或更新版本。当前设备暂用基础看板，升级后会自动取得你的设计。
        </p>
        <p v-if="paused" class="quiet-caption">展示已暂停</p>
        <p v-if="device.materialReason" class="notice subtle">
          {{ device.materialReason }}
        </p>
        <p v-if="device.error" class="notice warning">{{ device.error }}</p>
        <div class="device-controls">
          <button
            class="button primary"
            :disabled="syncDisabled"
            @click="control('sync')"
          >
            <RefreshCw :size="17" />立即同步这台电脑</button
          ><button
            class="button secondary"
            :disabled="frozen || device.localPaused"
            @click="control(device.remotePaused ? 'resume' : 'pause')"
          >
            <Play v-if="device.remotePaused" :size="17" /><Pause
              v-else
              :size="17"
            />{{ device.remotePaused ? "恢复看板" : "暂停看板" }}
          </button>
        </div>
        <div class="device-pagination">
          <p>
            设备最近上报页码<span>{{
              device.manualUntil > now
                ? `手动保持 · 剩余 ${Math.ceil((device.manualUntil - now) / 1000)} 秒`
                : `自动翻页 · 每页 ${classroom.layout.interval} 秒`
            }}</span>
          </p>
          <div>
            <button
              class="button secondary"
              :disabled="frozen || paused || device.page <= 1"
              @click="control('set_page', device.page - 1)"
            >
              <ChevronLeft :size="17" />上一页</button
            ><strong>{{ device.page }} / {{ device.pageCount }}</strong
            ><button
              class="button secondary"
              :disabled="frozen || paused || device.page >= device.pageCount"
              @click="control('set_page', device.page + 1)"
            >
              下一页<ChevronRight :size="17" />
            </button>
          </div>
          <button
            v-if="device.manualUntil > now"
            class="text-button"
            :disabled="frozen || paused"
            @click="control('auto_page')"
          >
            恢复自动翻页
          </button>
        </div>
        <p class="quiet-caption">样式上报于 {{ timeLabel(device.reported) }}</p>
        <div class="record-actions">
          <button class="text-button" @click="emit('design')">
            预览与调整布局</button
          ><button
            class="text-button destructive"
            :disabled="!allowed"
            @click="revoke"
          >
            撤销此设备
          </button>
        </div>
      </section>
      <section
        v-if="deviceView === 'history'"
        class="panel device-operation-history"
      >
        <div class="section-heading">
          <h2>最近操作</h2>
          <button
            class="icon-button"
            aria-label="查询设备最新状态"
            @click="run(status)"
          >
            <RefreshCw :size="17" />
          </button>
        </div>
        <p v-if="!history.length" class="inline-empty">暂无远程控制记录。</p>
        <article v-for="cmd in history" :key="cmd.id" class="command-row">
          <div>
            <strong>{{ actions[cmd.action] }}</strong
            ><span
              class="badge"
              :class="cmd.state === 'executed' ? '' : 'neutral'"
              >{{ states[cmd.state] ?? "结果待确认" }}</span
            >
          </div>
          <p>{{ cmd.detail }}</p>
          <small
            >{{ timeLabel(cmd.time, true) }} · {{ cmd.actor || "本班管理员" }} ·
            {{ cmd.id.slice(0, 8) }}</small
          >
        </article>
      </section>
    </div>
  </section>
  <dialog
    ref="renameDialog"
    class="modal"
    aria-labelledby="device-rename-title"
  >
    <header class="section-heading">
      <h2 id="device-rename-title">给电脑改名</h2>
      <button
        class="icon-button"
        aria-label="关闭改名"
        @click="renameDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <form @submit.prevent="saveName">
      <label class="field"
        >电脑名称<input
          v-model="renameName"
          required
          maxlength="60"
          placeholder="例如：六年级1班 · 讲台电脑"
          autofocus
      /></label>
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <button
        class="button primary full"
        :disabled="busy || !renameName.trim()"
      >
        {{ busy ? "正在保存…" : "保存名称" }}
      </button>
    </form>
  </dialog>
  <dialog ref="confirmation" class="modal">
    <header class="section-heading">
      <h2>确认设备操作</h2>
      <button
        class="icon-button"
        aria-label="关闭设备操作确认"
        @click="confirmation?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <p>{{ confirmText }}</p>
    <p v-if="error" class="field-error">{{ error }}</p>
    <div class="modal-actions">
      <button
        class="button secondary"
        :disabled="busy"
        @click="confirmation?.close()"
      >
        取消</button
      ><button class="button primary" :disabled="busy" @click="confirmed">
        确认
      </button>
    </div>
  </dialog>
</template>

<style scoped>
.connect-guide {
  list-style: none;
  margin: 24px 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 20px;
  text-align: left;
}
.connect-guide li {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.connect-guide li > span {
  background: #eaf2ec;
  color: var(--green);
  width: 25px;
  height: 25px;
  display: grid;
  place-items: center;
  border-radius: 50%;
  font-size: 12px;
  flex-shrink: 0;
}
.connect-guide li > div {
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.connect-guide strong {
  font-size: 13px;
  font-weight: 500;
}
.connect-guide small,
.connect-guide a {
  font-size: 12px;
}
.device-details {
  margin: 20px 0;
}
.device-details summary {
  cursor: pointer;
  font-size: 12px;
  color: var(--muted);
}
.device-details[open] summary {
  margin-bottom: 14px;
}
</style>
