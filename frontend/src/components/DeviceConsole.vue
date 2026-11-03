<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
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
} from "@lucide/vue";
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
}>();
const emit = defineEmits<{ refresh: []; bind: []; design: [] }>();
const selected = ref(""),
  device = computed(
    () =>
      props.devices.find((d) => d.id === selected.value) ?? props.devices[0],
  );
const allowed = computed(
  () => isAdmin(props.classroom) && !props.classroom.archived,
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
const frozen = computed(
  () =>
    !allowed.value ||
    !premiumAvailable(props.now) ||
    !device.value?.online ||
    !!pending.value ||
    busy.value,
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
  model.commands = [
    command,
    ...model.commands.filter((c) => c.id !== command.id),
  ];
  remember(null);
  notice.value = `${actions[command.action]}：${states[command.state] ?? "结果待确认"}。实际状态以设备回执为准。`;
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
async function control(action: string, page = 1) {
  if (!device.value || frozen.value) return;
  const d = device.value;
  const request: Pending = {
    key: uid(),
    deviceId: d.id,
    payload: {
      action,
      expectedControlRevision: d.revision,
      pageSetId: d.pageSetId ?? "",
      page,
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
const summary = computed(() => {
  const d = device.value;
  if (!d) return "";
  if (!d.online) return "设备暂时不可达";
  if (d.localPaused) return d.deferred ? "本机暂停待安静应用" : "本机已暂停";
  if (d.remotePaused) return "远程已暂停";
  if (d.deferred) return "已同步，等待安静应用";
  if (d.error) return "设备需要检查";
  return d.applied ? "看板运行中" : "等待首次应用";
});
let poll: ReturnType<typeof setInterval>;
let polling = false;
watch(
  () => props.classroom.id,
  () => {
    selected.value = "";
    restorePending();
  },
);
onMounted(() => {
  restorePending();
  poll = setInterval(async () => {
    if (polling || busy.value || confirmation.value?.open) return;
    polling = true;
    try {
      await status();
    } catch {
      /* 联系失败保留上次实际状态，不改报为成功。 */
    } finally {
      polling = false;
    }
  }, 5000);
});
onUnmounted(() => clearInterval(poll));
</script>

<template>
  <section class="device-console">
    <div v-if="error" class="notice danger" role="alert">{{ error }}</div>
    <div v-if="notice" class="notice subtle" role="status">{{ notice }}</div>
    <div v-if="pending" class="notice warning control-pending">
      <span>上一笔控制结果待确认，已保留原请求标识。</span
      ><button class="text-button" :disabled="busy" @click="run(queryPending)">
        查询原请求结果</button
      ><button class="text-button" :disabled="busy" @click="run(submitPending)">
        使用原标识安全重试
      </button>
    </div>
    <p v-if="!premiumAvailable(now)" class="notice subtle">
      设备绑定和自动同步免费可用。远程暂停、翻页及请求同步需要高级许可，可在“账号与许可”输入管理员提供的许可码。
    </p>
    <div class="section-heading">
      <div class="segmented">
        <button
          v-for="d in devices"
          :key="d.id"
          :class="{ selected: device?.id === d.id }"
          @click="selected = d.id"
        >
          <Laptop :size="16" />{{ d.name }}
        </button>
      </div>
      <button
        class="button secondary"
        :disabled="!allowed"
        @click="emit('bind')"
      >
        <Plus :size="17" />绑定教室电脑
      </button>
    </div>
    <section v-if="!device" class="panel empty-state">
      <Monitor :size="36" />
      <h2>让班级信息出现在教室桌面</h2>
      <p>在 Windows 客户端取得 6 位绑定码，<br />核对设备与班级后完成绑定。</p>
      <button class="button primary" :disabled="!allowed" @click="emit('bind')">
        输入绑定码
      </button>
    </section>
    <div v-else class="device-grid">
      <section class="panel">
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
                    ? "课件可能覆盖桌面；应用回执不代表学生已读。"
                    : "内容和布局仍可发布，设备恢复联网后同步。"
              }}
            </p>
          </div>
        </div>
        <dl class="device-facts">
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
        <p v-if="paused" class="quiet-caption">
          暂停时保留最近的看板样式记录，不代表当前壁纸。
        </p>
        <p v-if="device.materialReason" class="notice subtle">
          {{ device.materialReason }}
        </p>
        <p v-if="device.error" class="notice warning">{{ device.error }}</p>
        <div class="device-controls">
          <button
            class="button primary"
            :disabled="frozen"
            @click="control('sync')"
          >
            <RefreshCw :size="17" />请求同步</button
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
        <p class="quiet-caption">
          上报于 {{ timeLabel(device.reported) }}，不是实时截图。
        </p>
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
      <section class="panel">
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
