<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  KeyRound,
  Plus,
  Search,
  X,
  Copy,
  Check,
  ChevronLeft,
  ChevronRight,
  ShieldCheck,
  Clock3,
  RefreshCw,
} from "@lucide/vue";
import { api } from "../api";
import { adminDate } from "../admin";
const props = defineProps<{ initialUserId?: string }>();
const emit = defineEmits<{ changed: [] }>();
const focusUser = ref<any>(null),
  users = ref<any[]>([]),
  rows = ref<any[]>([]),
  issued = ref<any>(null);
const query = ref(""),
  searchTerm = ref(""),
  status = ref(""),
  offset = ref(0),
  total = ref(0),
  loading = ref(false),
  busy = ref(false),
  error = ref(""),
  formError = ref(""),
  copied = ref(false);
const issueDialog = ref<HTMLDialogElement>(),
  confirmDialog = ref<HTMLDialogElement>(),
  confirmation = ref<{ kind: string; item: any } | null>(null);
const userSearch = ref(""),
  form = reactive({ userId: "", days: 90 });
const selectedUser = computed(() =>
  users.value.find((u) => u.id === form.userId),
);
const canEnd = computed(
  () => focusUser.value?.premiumUntil > focusUser.value?.serverTime,
);
let requestSequence = 0;
function codeStatus(row: any) {
  return row.usedAt
    ? { label: "已兑换", tone: "green" }
    : row.revoked
      ? { label: "已撤销", tone: "muted" }
      : row.expired
        ? { label: "已过期", tone: "amber" }
        : { label: "待兑换", tone: "blue" };
}
async function load() {
  const sequence = ++requestSequence;
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({
      userId: focusUser.value?.id ?? "",
      search: searchTerm.value,
      status: status.value,
      offset: String(offset.value),
      limit: "20",
    });
    const data = await api(`/system/licenses?${params}`);
    if (sequence === requestSequence) {
      rows.value = data.items;
      total.value = data.total;
    }
  } catch (e) {
    if (sequence === requestSequence) error.value = (e as Error).message;
  } finally {
    if (sequence === requestSequence) loading.value = false;
  }
}
async function reload() {
  try {
    if (focusUser.value)
      focusUser.value = await api(`/system/users/${focusUser.value.id}`);
    await load();
  } catch (e) {
    error.value = (e as Error).message;
  }
}
onMounted(async () => {
  try {
    if (props.initialUserId)
      focusUser.value = await api(`/system/users/${props.initialUserId}`);
    await load();
  } catch (e) {
    error.value = (e as Error).message;
  }
});
function filter() {
  offset.value = 0;
  searchTerm.value = query.value.trim();
  void load();
}
function clearFocus() {
  focusUser.value = null;
  offset.value = 0;
  void load();
}
async function findUsers() {
  try {
    const params = new URLSearchParams({
      identity: "staff",
      status: "active",
      search: userSearch.value.trim(),
      limit: "100",
    });
    const data = await api(`/system/users?${params}`);
    users.value = data.items;
    if (
      focusUser.value &&
      !users.value.some((u) => u.id === focusUser.value.id) &&
      focusUser.value.active &&
      (!focusUser.value.expiresAt ||
        focusUser.value.expiresAt > focusUser.value.serverTime)
    )
      users.value.unshift(focusUser.value);
  } catch (e) {
    formError.value = (e as Error).message;
  }
}
async function openIssue() {
  issued.value = null;
  copied.value = false;
  formError.value = "";
  userSearch.value = "";
  form.userId = focusUser.value?.id ?? "";
  form.days = 90;
  issueDialog.value?.showModal();
  await findUsers();
}
async function issue() {
  busy.value = true;
  formError.value = "";
  try {
    issued.value = await api("/system/licenses", "POST", form);
    await load();
    emit("changed");
  } catch (e) {
    formError.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function copy() {
  try {
    await navigator.clipboard.writeText(
      `高级许可码：${issued.value.code}\n适用账号：${issued.value.login}\n许可时长：${issued.value.days} 天\n请在 ${adminDate(issued.value.expiresAt)} 前登录“我的账号”兑换。`,
    );
    copied.value = true;
  } catch {
    formError.value = "复制未成功，请选中许可码手动复制。";
  }
}
function confirm(kind: string, item: any) {
  confirmation.value = { kind, item };
  formError.value = "";
  confirmDialog.value?.showModal();
}
async function execute() {
  const action = confirmation.value!;
  busy.value = true;
  formError.value = "";
  try {
    await api(
      action.kind === "end"
        ? `/system/users/${action.item.id}/license/revoke`
        : `/system/licenses/${action.item.id}/revoke`,
      "POST",
    );
    await reload();
    confirmDialog.value?.close();
    emit("changed");
  } catch (e) {
    formError.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
defineExpose({ reload });
</script>
<template>
  <div class="admin-license-page">
    <div v-if="focusUser" class="admin-focus-banner">
      <span class="admin-avatar">{{ focusUser.name.slice(0, 1) }}</span>
      <div>
        <strong
          >{{ focusUser.name }} <span>{{ focusUser.login }}</span></strong
        >
        <p>
          {{
            canEnd
              ? `高级许可有效至 ${adminDate(focusUser.premiumUntil)}`
              : "正在使用免费基础功能"
          }}
        </p>
      </div>
      <button
        v-if="canEnd"
        class="text-button destructive"
        @click="confirm('end', focusUser)"
      >
        终止许可</button
      ><button class="button secondary" @click="clearFocus">
        查看全部老师
      </button>
    </div>
    <section class="admin-license-intro admin-card">
      <span class="license-symbol"><KeyRound :size="25" /></span>
      <div>
        <h2>把高级功能，交给需要的老师</h2>
        <p>
          许可码绑定指定账号，老师在“我的账号”中兑换。有效期从兑换当天开始计算。
        </p>
        <div class="license-rules">
          <span><ShieldCheck :size="14" />一人一码</span
          ><span><Clock3 :size="14" />30 天内兑换</span
          ><span><Check :size="14" />续期自动累加</span>
        </div>
      </div>
      <button class="button primary" @click="openIssue">
        <Plus :size="17" />发放许可
      </button>
    </section>
    <p v-if="error" class="admin-feedback error" role="alert">
      {{ error }}<button class="text-button" @click="load">重试</button>
    </p>
    <section class="admin-card admin-list-card" :aria-busy="loading">
      <header class="admin-card-heading">
        <div>
          <h2>
            {{ focusUser ? `${focusUser.name}的许可记录` : "全部许可记录" }}
          </h2>
          <p>跟踪发放、兑换与失效状态</p>
        </div>
        <button
          class="icon-button"
          aria-label="刷新许可记录"
          :disabled="loading"
          @click="reload"
        >
          <RefreshCw :size="17" :class="{ spinning: loading }" />
        </button>
      </header>
      <div class="admin-filterbar">
        <form class="admin-search" @submit.prevent="filter">
          <Search :size="17" /><input
            v-model="query"
            placeholder="搜索老师姓名或账号"
            aria-label="搜索许可记录"
          /><button type="submit">搜索</button>
        </form>
        <div class="admin-filter-selects">
          <select v-model="status" aria-label="许可码状态" @change="filter">
            <option value="">全部状态</option>
            <option value="pending">待兑换</option>
            <option value="used">已兑换</option>
            <option value="revoked">已撤销</option>
            <option value="expired">已过期</option></select
          ><button
            v-if="status || searchTerm"
            class="text-button"
            @click="
              status = '';
              query = '';
              filter();
            "
          >
            重置
          </button>
        </div>
      </div>
      <div v-if="loading" class="admin-loading-line" role="status">
        正在加载…
      </div>
      <div v-if="!rows.length && !loading" class="admin-empty">
        <KeyRound :size="31" />
        <h3>
          {{ status || searchTerm ? "没有匹配的许可记录" : "还没有发放许可" }}
        </h3>
        <p>基础功能免费可用，需要高级功能时再发放许可。</p>
        <button class="button secondary" @click="openIssue">发放许可</button>
      </div>
      <div v-else class="admin-table-wrap">
        <table class="admin-table">
          <thead>
            <tr>
              <th>适用老师</th>
              <th>许可时长</th>
              <th>兑换状态</th>
              <th>兑换截止</th>
              <th>兑换时间</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in rows" :key="row.id">
              <td data-label="适用老师" class="primary-cell">
                <span class="admin-avatar small">{{
                  row.name.slice(0, 1)
                }}</span>
                <div class="table-person">
                  <strong>{{ row.name }}</strong
                  ><small>{{ row.login }}</small>
                </div>
              </td>
              <td data-label="许可时长">
                <strong>{{ row.days }}</strong> 天
              </td>
              <td data-label="兑换状态">
                <span class="admin-pill" :class="codeStatus(row).tone">{{
                  codeStatus(row).label
                }}</span>
              </td>
              <td data-label="兑换截止">{{ adminDate(row.expiresAt) }}</td>
              <td data-label="兑换时间">{{ adminDate(row.usedAt, true) }}</td>
              <td data-label="操作" class="table-actions">
                <button
                  v-if="!row.usedAt && !row.revoked && !row.expired"
                  class="destructive"
                  @click="confirm('code', row)"
                >
                  撤销许可码</button
                ><span v-else class="muted">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <footer class="admin-pagination">
        <span>共 {{ total }} 条记录</span>
        <div>
          <span class="muted">20 条 / 页</span
          ><button
            class="icon-button"
            aria-label="许可上一页"
            :disabled="loading || offset === 0"
            @click="
              offset -= 20;
              load();
            "
          >
            <ChevronLeft :size="17" /></button
          ><span class="page-number">{{ Math.floor(offset / 20) + 1 }}</span
          ><button
            class="icon-button"
            aria-label="许可下一页"
            :disabled="loading || offset + 20 >= total"
            @click="
              offset += 20;
              load();
            "
          >
            <ChevronRight :size="17" />
          </button>
        </div>
      </footer>
    </section>
    <dialog
      ref="issueDialog"
      class="admin-drawer"
      @cancel="busy && $event.preventDefault()"
      @close="issued = null"
    >
      <header>
        <div>
          <span class="admin-mini-label">高级许可</span>
          <h2>{{ issued ? "许可已生成" : "发放高级许可" }}</h2>
        </div>
        <button
          class="icon-button"
          aria-label="关闭发放许可"
          :disabled="busy"
          @click="issueDialog?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <div v-if="issued" class="admin-drawer-body license-issued">
        <span class="admin-result-icon"><Check :size="27" /></span>
        <h3>现在可以发给老师了</h3>
        <p>以下许可码只显示这一次，请复制并妥善保存。</p>
        <div class="issued-code">
          <span>{{ issued.login }} · {{ issued.days }} 天高级许可</span
          ><code>{{ issued.code }}</code
          ><small
            >兑换截止：{{
              adminDate(issued.expiresAt)
            }}。只能由指定账号兑换。</small
          >
        </div>
        <button class="button primary" @click="copy">
          <component :is="copied ? Check : Copy" :size="16" />{{
            copied ? "已复制许可信息" : "复制许可信息"
          }}
        </button>
        <div class="admin-tip">
          <Clock3 :size="18" />
          <p>兑换后立即生效；已有许可未到期时，新增天数接在原到期日之后。</p>
        </div>
        <p v-if="formError" class="field-error" role="alert">{{ formError }}</p>
      </div>
      <form v-else class="admin-drawer-form" @submit.prevent="issue">
        <fieldset :disabled="busy" class="admin-drawer-body">
          <h3>1. 选择老师</h3>
          <div class="admin-search">
            <Search :size="17" /><input
              v-model="userSearch"
              placeholder="按姓名或账号查找"
              aria-label="搜索许可接收人"
              @keydown.enter.prevent="findUsers"
            /><button type="button" @click="findUsers">查找</button>
          </div>
          <label class="field"
            >许可接收人<select v-model="form.userId" required>
              <option disabled value="">请选择班主任或普通老师</option>
              <option v-for="user in users" :key="user.id" :value="user.id">
                {{ user.name }} · {{ user.login }}
              </option>
            </select></label
          >
          <p class="admin-field-help">
            只显示可正常登录的教师账号，系统管理员直接享有全部功能。
          </p>
          <div v-if="selectedUser" class="admin-selection-summary">
            <span>当前高级许可</span
            ><strong>{{
              selectedUser.premiumUntil > selectedUser.serverTime
                ? `有效至 ${adminDate(selectedUser.premiumUntil)}`
                : "尚未开通 / 已到期"
            }}</strong>
          </div>
          <h3>2. 设置许可时长</h3>
          <div class="admin-duration-options">
            <button
              v-for="days in [30, 90, 180, 365]"
              :key="days"
              type="button"
              :class="{ selected: form.days === days }"
              @click="form.days = days"
            >
              {{ days }} 天
            </button>
          </div>
          <label class="field"
            >自定义天数<input
              type="number"
              min="1"
              max="3660"
              step="1"
              v-model.number="form.days"
              required
          /></label>
          <div class="admin-tip">
            <ShieldCheck :size="18" />
            <p>许可只解锁高级功能，不会延长账号本身的可用期限。</p>
          </div>
          <p v-if="formError" class="field-error" role="alert">
            {{ formError }}
          </p>
        </fieldset>
        <footer>
          <button
            type="button"
            class="button secondary"
            :disabled="busy"
            @click="issueDialog?.close()"
          >
            取消</button
          ><button class="button primary" :disabled="busy || !form.userId">
            {{ busy ? "正在生成…" : "生成许可码" }}
          </button>
        </footer>
      </form>
    </dialog>
    <dialog
      ref="confirmDialog"
      class="admin-confirm-dialog"
      @cancel="busy && $event.preventDefault()"
    >
      <div class="admin-confirm-content">
        <span class="metric-icon amber"><KeyRound :size="23" /></span>
        <h2>
          {{
            confirmation?.kind === "end"
              ? "终止当前高级许可？"
              : "撤销这张许可码？"
          }}
        </h2>
        <p v-if="confirmation?.kind === 'end'">
          {{
            confirmation.item.name
          }}将恢复免费基础功能，同时撤销该账号尚未兑换的许可码。教学数据会保留。
        </p>
        <p v-else>
          撤销后，{{
            confirmation?.item.name
          }}将无法兑换这张许可码，已有的高级许可不受影响。
        </p>
        <p v-if="formError" class="field-error" role="alert">{{ formError }}</p>
      </div>
      <footer>
        <button
          class="button secondary"
          :disabled="busy"
          @click="confirmDialog?.close()"
        >
          取消</button
        ><button class="button danger" :disabled="busy" @click="execute">
          {{ busy ? "正在处理…" : "确认撤销" }}
        </button>
      </footer>
    </dialog>
  </div>
</template>
