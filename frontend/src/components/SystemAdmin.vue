<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  Users,
  School,
  Monitor,
  Database,
  Settings,
  ScrollText,
  RefreshCw,
  Plus,
  Download,
  X,
} from "@lucide/vue";
import { api, session } from "../api";
import { timeLabel } from "../domain";
import AccountOnboarding from "./AccountOnboarding.vue";
import LicenseManager from "./LicenseManager.vue";

const emit = defineEmits<{
  changed: [];
  restored: [string];
  createClass: [];
}>();
const tabs = [
  { id: "overview", name: "运行概况", icon: Monitor },
  { id: "users", name: "账号管理", icon: Users },
  { id: "onboarding", name: "创建账号", icon: Plus },
  { id: "licenses", name: "高级许可", icon: ScrollText },
  { id: "classes", name: "全部班级", icon: School },
  { id: "devices", name: "全部设备", icon: Monitor },
  { id: "settings", name: "站点设置", icon: Settings },
  { id: "backups", name: "备份恢复", icon: Database },
  { id: "audit", name: "系统审计", icon: ScrollText },
];
const tab = ref("overview"),
  overview = ref<any>(null),
  rows = ref<any[]>([]),
  total = ref(0),
  offset = ref(0),
  search = ref("");
const busy = ref(false),
  error = ref(""),
  notice = ref(""),
  backupSupported = ref(true);
const settingsForm = reactive({ siteName: "", registrationOpen: false });
const modal = ref<HTMLDialogElement>(),
  mode = ref(""),
  target = ref<any>(null);
const userForm = reactive({
  login: "",
  name: "",
  password: "",
  systemAdmin: false,
  active: true,
  canCreateClasses: false,
  expiry: "",
});
const licenseUserId = ref("");
const classForm = reactive({ name: "", ownerId: "", archived: false }),
  ownerSearch = ref(""),
  ownerOptions = ref<any[]>([]);
const restoreForm = reactive({ password: "", confirmation: "" });
const paged = computed(() =>
  ["users", "classes", "devices", "audit"].includes(tab.value),
);
const auditNames: Record<string, string> = {
  system_initialized: "初始化系统",
  system_settings_changed: "修改站点设置",
  user_created: "创建账号",
  account_created: "创建账号",
  account_invited: "旧版账号邀请",
  account_activated: "账号完成激活",
  activation_reissued: "重发激活码",
  license_issued: "签发高级许可",
  license_redeemed: "兑换高级许可",
  license_revoked: "作废许可码",
  license_ended: "终止高级许可",
  user_updated: "修改账号",
  password_reset: "重置密码",
  class_updated: "修改班级",
  device_revoked: "撤销设备",
  backup_created: "创建备份",
  backup_restored: "恢复备份",
};
const modalNames: Record<string, string> = {
  create: "创建账号",
  edit: "编辑账号",
  password: "重置密码",
  class: "管理班级",
  revoke: "撤销设备",
  restore: "恢复数据库备份",
};
const size = (bytes: number) =>
  bytes >= 1073741824
    ? `${(bytes / 1073741824).toFixed(1)} GB`
    : `${(bytes / 1048576).toFixed(1)} MB`;
async function run(action: () => Promise<void>) {
  busy.value = true;
  error.value = "";
  notice.value = "";
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function load() {
  overview.value = await api("/system/overview");
  Object.assign(settingsForm, {
    siteName: overview.value.siteName,
    registrationOpen: overview.value.registrationOpen,
  });
  if (paged.value) {
    const result = await api(
      `/system/${tab.value}?${new URLSearchParams({ search: search.value, offset: String(offset.value), limit: "20" })}`,
    );
    rows.value = result.items;
    total.value = result.total;
  } else if (tab.value === "backups") {
    const result = await api("/system/backups");
    rows.value = result.items;
    backupSupported.value = result.supported;
  }
}
async function switchTab(id: string) {
  tab.value = id;
  search.value = "";
  offset.value = 0;
  rows.value = [];
  await run(load);
}
onMounted(() => run(load));
function open(kind: string, item: any = null) {
  mode.value = kind;
  target.value = item;
  error.value = "";
  Object.assign(userForm, {
    login: item?.login ?? "",
    name: item?.name ?? "",
    password: "",
    active: item?.active ?? true,
    systemAdmin: item?.systemAdmin ?? false,
    canCreateClasses: item?.canCreateClasses ?? false,
    expiry: item?.expiresAt
      ? new Date(item.expiresAt * 1000 + 8 * 3600000).toISOString().slice(0, 10)
      : "",
  });
  if (kind === "class") {
    Object.assign(classForm, {
      name: item.name,
      ownerId: item.owner.id,
      archived: item.archived,
    });
    ownerOptions.value = [item.owner];
    ownerSearch.value = "";
  }
  restoreForm.password = restoreForm.confirmation = "";
  modal.value?.showModal();
}
async function findOwners() {
  await run(async () => {
    const result = await api(
      `/system/users?search=${encodeURIComponent(ownerSearch.value)}&limit=100`,
    );
    ownerOptions.value = result.items.filter((u: any) => u.active);
    if (
      target.value?.owner &&
      !ownerOptions.value.some((u) => u.id === target.value.owner.id)
    )
      ownerOptions.value.unshift(target.value.owner);
  });
}
async function save() {
  await run(async () => {
    if (mode.value === "create") {
      const { active, ...body } = userForm;
      void active;
      await api("/system/users", "POST", body);
    } else if (mode.value === "edit") {
      await api(`/system/users/${target.value.id}`, "PUT", {
        name: userForm.name,
        active: userForm.active,
        systemAdmin: userForm.systemAdmin,
        canCreateClasses: userForm.canCreateClasses,
        expiresAt: userForm.expiry
          ? new Date(`${userForm.expiry}T23:59:59+08:00`).getTime() / 1000
          : 0,
      });
    } else if (mode.value === "password") {
      await api(`/system/users/${target.value.id}/reset-password`, "POST", {
        password: userForm.password,
      });
      if (target.value.id === session.userId) {
        modal.value?.close();
        emit("restored", "密码已重置，请重新登录。");
        return;
      }
    } else if (mode.value === "class") {
      await api(`/system/classes/${target.value.id}`, "PUT", classForm);
    } else if (mode.value === "revoke") {
      await api(`/system/devices/${target.value.id}/revoke`, "POST");
    } else if (mode.value === "restore") {
      const result = await api(
        `/system/backups/${encodeURIComponent(target.value.name)}/restore`,
        "POST",
        restoreForm,
      );
      modal.value?.close();
      emit("restored", result.message);
      return;
    }
    modal.value?.close();
    userForm.password = "";
    await load();
    emit("changed");
    notice.value = "操作已保存。";
  });
}
async function saveSettings() {
  await run(async () => {
    await api("/system/settings", "PUT", settingsForm);
    await load();
    emit("changed");
    notice.value = "站点设置已保存。";
  });
}
async function createBackup() {
  await run(async () => {
    await api("/system/backups", "POST");
    await load();
    notice.value = "备份已生成，可以下载到其他位置保存。";
  });
}
async function download(name: string) {
  await run(async () => {
    const response = await fetch(
      `/api/system/backups/${encodeURIComponent(name)}/download`,
      { headers: { Authorization: `Bearer ${session.token}` } },
    );
    if (!response.ok) throw new Error("下载失败，请重新登录后重试。");
    const url = URL.createObjectURL(await response.blob()),
      link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
}
</script>

<template>
  <section class="system-page">
    <header class="page-heading">
      <div>
        <p class="eyebrow">
          {{ overview?.siteName ?? "班级看板" }} · 系统管理员
        </p>
        <h1>系统管理</h1>
        <p class="muted">维护全站账号、班级、设备和数据。</p>
      </div>
      <button class="button secondary" :disabled="busy" @click="run(load)">
        <RefreshCw :size="17" />刷新
      </button>
    </header>
    <nav class="system-tabs" aria-label="系统管理导航">
      <button
        v-for="item in tabs"
        :key="item.id"
        :class="{ active: tab === item.id }"
        :disabled="busy"
        @click="switchTab(item.id)"
      >
        <component :is="item.icon" :size="17" />{{ item.name }}
      </button>
    </nav>
    <p v-if="error && !modal?.open" class="notice danger" role="alert">
      {{ error }}
    </p>
    <p v-if="notice" class="notice subtle" role="status">{{ notice }}</p>
    <template v-if="overview">
      <AccountOnboarding
        v-if="tab === 'onboarding'"
        @changed="emit('changed')"
      />
      <LicenseManager
        v-else-if="tab === 'licenses'"
        :initial-user-id="licenseUserId"
        @changed="emit('changed')"
      />
      <template v-else-if="tab === 'overview'">
        <div class="system-stats">
          <article class="panel">
            <span>教师与管理员</span><strong>{{ overview.users }}</strong
            ><small>{{ overview.activeUsers }} 个启用账号</small>
          </article>
          <article class="panel">
            <span>使用中的班级</span><strong>{{ overview.classes }}</strong
            ><small>{{ overview.archivedClasses }} 个已归档</small>
          </article>
          <article class="panel">
            <span>已绑定设备</span><strong>{{ overview.devices }}</strong
            ><small>{{ overview.onlineDevices }} 台在线</small>
          </article>
          <article class="panel">
            <span>可用存储</span
            ><strong>{{ size(overview.diskFreeBytes) }}</strong
            ><small>{{
              overview.database === "sqlite" ? "SQLite 单机运行" : "PostgreSQL"
            }}</small>
          </article>
        </div>
        <section class="panel admin-start">
          <h2>开通老师的工作台</h2>
          <p class="muted">
            设置账号、密码和任教班级，老师可以直接登录。高级功能的许可码在这里单独管理。
          </p>
          <div class="record-actions">
            <button class="button primary" @click="switchTab('onboarding')">
              开通班主任 / 老师账号</button
            ><button
              class="button secondary"
              @click="
                licenseUserId = '';
                switchTab('licenses');
              "
            >
              签发高级许可
            </button>
          </div>
        </section>
        <section class="panel">
          <h2>服务状态</h2>
          <dl class="system-details">
            <div>
              <dt>站点地址</dt>
              <dd>{{ overview.publicUrl }}</dd>
            </div>
            <div>
              <dt>本次运行</dt>
              <dd>{{ Math.floor(overview.uptimeSeconds / 60) }} 分钟</dd>
            </div>
            <div>
              <dt>数据库大小</dt>
              <dd>
                {{
                  overview.databaseBytes === null
                    ? "由数据库服务管理"
                    : size(overview.databaseBytes)
                }}
              </dd>
            </div>
            <div>
              <dt>服务时间</dt>
              <dd>{{ timeLabel(overview.serverTime * 1000, true) }}</dd>
            </div>
            <div>
              <dt>账号获取</dt>
              <dd>由管理员创建</dd>
            </div>
            <div>
              <dt>服务器管理</dt>
              <dd>通过服务器菜单管理</dd>
            </div>
          </dl>
        </section>
      </template>
      <section v-else-if="tab === 'settings'" class="panel system-form">
        <h2>站点设置</h2>
        <form @submit.prevent="saveSettings">
          <fieldset :disabled="busy">
            <label class="field"
              >站点名称<input
                v-model.trim="settingsForm.siteName"
                required
                maxlength="80"
            /></label>
            <p class="muted">
              老师使用管理员分配的账号和密码登录。身份、账号期限和高级许可均由系统管理员管理。
            </p>
            <label class="field"
              >公开访问地址<input :value="overview.publicUrl" readonly
            /></label>
            <p class="muted small">
              更换域名时，在服务器部署配置中修改公开地址。
            </p>
            <button class="button primary" :disabled="busy">
              保存站点设置
            </button>
          </fieldset>
        </form>
      </section>
      <section v-else-if="tab === 'backups'" class="panel">
        <div class="section-heading">
          <h2>数据库备份</h2>
          <button
            class="button primary"
            :disabled="busy || !backupSupported"
            @click="createBackup"
          >
            <Plus :size="16" />立即备份
          </button>
        </div>
        <p class="muted">
          备份包含全站业务数据。恢复会替换当前数据并注销所有账号和设备；操作前自动保留当前数据库副本。
        </p>
        <p v-if="!backupSupported" class="notice">
          PostgreSQL 请通过服务器运维工具执行备份和恢复。
        </p>
        <p v-if="!rows.length" class="inline-empty">暂无备份。</p>
        <article v-for="item in rows" :key="item.name" class="system-row">
          <div>
            <strong>{{ timeLabel(item.at * 1000, true) }}</strong>
            <p class="muted small">{{ item.name }} · {{ size(item.bytes) }}</p>
          </div>
          <div class="record-actions">
            <button
              class="button secondary"
              :disabled="busy"
              @click="download(item.name)"
            >
              <Download :size="16" />下载</button
            ><button
              class="text-button destructive"
              :disabled="busy || !backupSupported"
              @click="open('restore', item)"
            >
              恢复此备份
            </button>
          </div>
        </article>
      </section>
      <section v-else class="panel">
        <div class="system-toolbar">
          <button
            v-if="tab === 'classes'"
            class="button primary"
            @click="emit('createClass')"
          >
            创建班级
          </button>
          <form
            v-if="tab !== 'audit'"
            class="system-search"
            @submit.prevent="
              offset = 0;
              run(load);
            "
          >
            <label class="sr-only" for="system-search"
              >搜索{{ tabs.find((t) => t.id === tab)?.name }}</label
            ><input
              id="system-search"
              v-model="search"
              placeholder="输入姓名、账号或名称"
            /><button class="button secondary" :disabled="busy">搜索</button>
          </form>
          <button
            v-if="tab === 'users'"
            class="button primary"
            :disabled="busy"
            @click="switchTab('onboarding')"
          >
            <Plus :size="16" />创建账号
          </button>
        </div>
        <p v-if="!rows.length" class="inline-empty">
          {{ busy ? "正在读取…" : "没有符合条件的记录。" }}
        </p>
        <article v-for="item in rows" :key="item.id" class="system-row">
          <template v-if="tab === 'users'"
            ><div>
              <h3>
                {{ item.name }}
                <span class="badge neutral">{{
                  item.systemAdmin
                    ? "系统管理员"
                    : item.canCreateClasses
                      ? "班主任"
                      : "普通老师"
                }}</span
                ><span v-if="!item.active" class="badge">已停用</span
                ><span v-else-if="!item.activated" class="badge amber-badge"
                  >请设置密码</span
                >
              </h3>
              <p class="muted">{{ item.login }}</p>
              <p class="muted small">
                账号：{{
                  item.expiresAt
                    ? timeLabel(item.expiresAt * 1000, true) + " 到期"
                    : "长期有效"
                }}
                ·
                {{
                  item.systemAdmin
                    ? "最高管理权限"
                    : item.premiumActive
                      ? "高级许可有效"
                      : "基础版"
                }}
              </p>
            </div>
            <div class="record-actions">
              <button
                class="button secondary"
                :disabled="busy"
                @click="open('edit', item)"
              >
                编辑账号</button
              ><button
                v-if="!item.systemAdmin"
                class="text-button"
                @click="
                  licenseUserId = item.id;
                  switchTab('licenses');
                "
              >
                管理许可</button
              ><button
                class="text-button"
                :disabled="busy"
                @click="open('password', item)"
              >
                重置密码
              </button>
            </div></template
          >
          <template v-else-if="tab === 'classes'"
            ><div>
              <h3>
                {{ item.name }}
                <span class="badge neutral">{{
                  item.archived ? "已归档" : item.stage
                }}</span>
              </h3>
              <p class="muted">
                所有者 {{ item.owner.name }}（{{ item.owner.login }}） ·
                {{ item.members }} 位成员
              </p>
            </div>
            <button
              class="button secondary"
              :disabled="busy"
              @click="open('class', item)"
            >
              管理班级
            </button></template
          >
          <template v-else-if="tab === 'devices'"
            ><div>
              <h3>
                {{ item.name }}
                <span class="badge neutral">{{
                  item.revoked ? "已撤销" : item.online ? "在线" : "离线"
                }}</span>
              </h3>
              <p class="muted">
                {{ item.className }} · 缓存 v{{ item.cached }} / 应用 v{{
                  item.applied
                }}
              </p>
              <p class="muted small">
                最近联系 {{ timeLabel(item.reported, true) }}
              </p>
            </div>
            <button
              class="text-button destructive"
              :disabled="busy || item.revoked"
              @click="open('revoke', item)"
            >
              撤销设备
            </button></template
          >
          <template v-else
            ><div>
              <h3>{{ auditNames[item.action] ?? item.action }}</h3>
              <p class="muted">{{ item.actor }} · {{ item.target }}</p>
              <p class="muted small">{{ timeLabel(item.at * 1000, true) }}</p>
            </div></template
          >
        </article>
        <footer class="records-pagination">
          <span
            >共 {{ total }} 条 · 第 {{ Math.floor(offset / 20) + 1 }} 页</span
          >
          <div class="record-actions">
            <button
              class="button secondary"
              :disabled="busy || offset === 0"
              @click="
                offset -= 20;
                run(load);
              "
            >
              上一页</button
            ><button
              class="button secondary"
              :disabled="busy || offset + 20 >= total"
              @click="
                offset += 20;
                run(load);
              "
            >
              下一页
            </button>
          </div>
        </footer>
      </section>
    </template>
    <dialog
      ref="modal"
      class="modal system-modal"
      @cancel="busy && $event.preventDefault()"
    >
      <header class="section-heading">
        <h2>{{ modalNames[mode] }}</h2>
        <button
          class="icon-button"
          aria-label="关闭系统操作"
          :disabled="busy"
          @click="modal?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <form @submit.prevent="save">
        <template v-if="mode === 'create' || mode === 'edit'"
          ><label v-if="mode === 'create'" class="field"
            >账号<input
              v-model.trim="userForm.login"
              required
              minlength="3"
              maxlength="80"
              pattern="[a-zA-Z0-9_@.\-]+" /></label
          ><label class="field"
            >姓名<input
              v-model.trim="userForm.name"
              required
              maxlength="40" /></label
          ><label v-if="mode === 'create'" class="field"
            >初始密码<input
              type="password"
              v-model="userForm.password"
              required
              minlength="10"
              maxlength="128"
              autocomplete="new-password" /></label
          ><label class="system-check"
            ><input
              type="checkbox"
              v-model="userForm.systemAdmin"
              :disabled="target?.id === session.userId"
            />系统管理员</label
          ><label class="system-check" v-if="!userForm.systemAdmin"
            ><input
              type="checkbox"
              v-model="userForm.canCreateClasses"
            />班主任：允许创建班级</label
          ><label class="field"
            >账号到期日（北京时间，留空为长期有效）<input
              type="date"
              v-model="userForm.expiry"
              :disabled="target?.id === session.userId" /></label
          ><label v-if="mode === 'edit'" class="system-check"
            ><input
              type="checkbox"
              v-model="userForm.active"
              :disabled="target?.id === session.userId"
            />账号启用</label
          >
          <p class="muted small">
            停用账号会立即撤销登录；其班级和历史内容保留。
          </p></template
        >
        <template v-if="mode === 'password'"
          ><p class="muted">
            为 {{ target?.name }}（{{
              target?.login
            }}）设置新密码，原登录会话将失效。
          </p>
          <label class="field"
            >新密码<input
              type="password"
              v-model="userForm.password"
              required
              minlength="10"
              maxlength="128"
              autocomplete="new-password" /></label
        ></template>
        <template v-if="mode === 'class'"
          ><label class="field"
            >班级名称<input
              v-model.trim="classForm.name"
              required
              maxlength="60" /></label
          ><label class="field"
            >查找接任账号<input
              v-model="ownerSearch"
              placeholder="姓名或账号" /></label
          ><button
            type="button"
            class="text-button"
            :disabled="busy"
            @click="findOwners"
          >
            查找账号</button
          ><label class="field"
            >班级所有者<select v-model="classForm.ownerId" required>
              <option
                v-for="owner in ownerOptions"
                :key="owner.id"
                :value="owner.id"
              >
                {{ owner.name }} · {{ owner.login }}
              </option>
            </select></label
          ><label class="system-check"
            ><input
              type="checkbox"
              v-model="classForm.archived"
            />归档班级</label
          >
          <p class="muted small">
            归档撤销设备绑定；恢复班级后需重新绑定。更换所有者后，原所有者保留班级管理员身份。
          </p></template
        >
        <p v-if="mode === 'revoke'" class="notice warning">
          撤销 {{ target?.name }} 后，该设备需重新配对才能获取班级内容。
        </p>
        <template v-if="mode === 'restore'"
          ><p class="notice warning">
            用
            {{ target?.name }}
            替换全站数据。恢复后使用备份中的账号和密码重新登录，所有教室设备需重新绑定。
          </p>
          <label class="field"
            >当前管理员密码<input
              type="password"
              v-model="restoreForm.password"
              required
              autocomplete="current-password" /></label
          ><label class="field"
            >输入“恢复备份”确认<input
              v-model="restoreForm.confirmation"
              required
              pattern="恢复备份"
              autocomplete="off" /></label
        ></template>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
        <button class="button primary full" :disabled="busy">
          {{
            busy
              ? "正在处理…"
              : mode === "restore"
                ? "确认恢复备份"
                : "保存并确认"
          }}
        </button>
      </form>
    </dialog>
  </section>
</template>
