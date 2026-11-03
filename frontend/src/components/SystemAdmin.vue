<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import {
  LayoutDashboard,
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
  Search,
  ChevronRight,
  ChevronLeft,
  ArrowUpRight,
  LogOut,
  ShieldCheck,
  KeyRound,
  Menu,
  SlidersHorizontal,
  Inbox,
  Check,
} from "@lucide/vue";
import { api, session } from "../api";
import { adminDate, auditNames, fileSize } from "../admin";
import AccountOnboarding from "./AccountOnboarding.vue";
import LicenseManager from "./LicenseManager.vue";
import AdminOverview from "./AdminOverview.vue";
import AdminDataTable from "./AdminDataTable.vue";
import AdminEditDrawer from "./AdminEditDrawer.vue";
const emit = defineEmits<{
  changed: [];
  restored: [string];
  createClass: [];
  openClass: [string];
  teaching: [];
  logout: [];
}>();
const sections = [
  {
    id: "overview",
    name: "管理概览",
    icon: LayoutDashboard,
    group: "工作台",
  },
  {
    id: "users",
    name: "账号管理",
    icon: Users,
    group: "学校管理",
  },
  {
    id: "classes",
    name: "班级管理",
    icon: School,
    group: "学校管理",
  },
  {
    id: "devices",
    name: "教室设备",
    icon: Monitor,
    group: "学校管理",
  },
  {
    id: "licenses",
    name: "高级许可",
    icon: KeyRound,
    group: "授权与数据",
  },
  {
    id: "backups",
    name: "数据备份",
    icon: Database,
    group: "授权与数据",
  },
  {
    id: "audit",
    name: "操作记录",
    icon: ScrollText,
    group: "授权与数据",
  },
  {
    id: "settings",
    name: "站点设置",
    icon: Settings,
    group: "系统",
  },
];
const groups = [...new Set(sections.map((s) => s.group))];
const fromHash = () => {
  const v = location.hash.replace("#/admin/", "");
  return sections.some((s) => s.id === v) ? v : "overview";
};
const tab = ref(fromHash()),
  mobileMenu = ref(false),
  overview = ref<any>(null),
  rows = ref<any[]>([]),
  activity = ref<any[]>([]);
const total = ref(0),
  offset = ref(0),
  pageSize = ref(20),
  query = ref("");
const filters = reactive({
  search: "",
  identity: "",
  status: "",
  license: "",
  action: "",
});
const busy = ref(false),
  loading = ref(false),
  error = ref(""),
  notice = ref(""),
  backupSupported = ref(true);
const activeSection = computed(() => sections.find((s) => s.id === tab.value)!);
const hasFilters = computed(() => Object.values(filters).some(Boolean));
const settingsForm = reactive({ siteName: "" }),
  licenseUserId = ref("");
const creationDialog = ref<HTMLDialogElement>(),
  creationMounted = ref(false),
  creationBusy = ref(false);
const editDrawer = ref<InstanceType<typeof AdminEditDrawer>>();
const licenseView = ref<InstanceType<typeof LicenseManager>>();
async function refreshPage() {
  await load(true);
  if (tab.value === "licenses") await licenseView.value?.reload();
}
const statusOptions = computed(() =>
  tab.value === "users"
    ? [
        ["active", "正常"],
        ["expiring", "7 天内到期"],
        ["expired", "已到期"],
        ["disabled", "已停用"],
        ["pending", "待设密码"],
      ]
    : tab.value === "classes"
      ? [
          ["active", "使用中"],
          ["archived", "已归档"],
        ]
      : [
          ["online", "在线"],
          ["offline", "离线"],
          ["revoked", "已解绑"],
        ],
);
let requestSequence = 0;
async function load(withOverview = false) {
  const sequence = ++requestSequence,
    currentTab = tab.value;
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({
      ...filters,
      offset: String(offset.value),
      limit: String(pageSize.value),
    });
    const path =
      currentTab === "overview"
        ? "/system/audit?limit=5"
        : ["users", "classes", "devices", "audit"].includes(currentTab)
          ? `/system/${currentTab}?${params}`
          : currentTab === "backups"
            ? "/system/backups"
            : "";
    const [summary, result] = await Promise.all([
      withOverview || !overview.value ? api("/system/overview") : null,
      path ? api(path) : null,
    ]);
    // 快速切换筛选时，只呈现最后一次请求的结果，避免旧结果覆盖当前页面。
    if (sequence !== requestSequence) return;
    if (summary) {
      overview.value = summary;
      settingsForm.siteName = summary.siteName;
    }
    if (currentTab === "overview") activity.value = result.items;
    else if (result) {
      rows.value = result.items;
      total.value = result.total ?? result.items.length;
      if (currentTab === "backups") backupSupported.value = result.supported;
    }
  } catch (e) {
    if (sequence === requestSequence) error.value = (e as Error).message;
  } finally {
    if (sequence === requestSequence) loading.value = false;
  }
}
async function go(
  id: string,
  values: Record<string, string> = {},
  push = true,
) {
  tab.value = id;
  mobileMenu.value = false;
  query.value = "";
  offset.value = 0;
  rows.value = [];
  total.value = 0;
  notice.value = "";
  Object.assign(
    filters,
    { search: "", identity: "", status: "", license: "", action: "" },
    values,
  );
  if (id !== "licenses") licenseUserId.value = "";
  if (push) history.pushState(null, "", `#/admin/${id}`);
  await load(id === "overview");
}
function popstate() {
  void go(fromHash(), {}, false);
}
onMounted(() => {
  window.addEventListener("popstate", popstate);
  void load(true);
});
onUnmounted(() => {
  window.removeEventListener("popstate", popstate);
  requestSequence++;
});
function filter() {
  offset.value = 0;
  void load();
}
function search() {
  filters.search = query.value.trim();
  filter();
}
function resetFilters() {
  query.value = "";
  Object.assign(filters, {
    search: "",
    identity: "",
    status: "",
    license: "",
    action: "",
  });
  filter();
}
function paginate(direction: number) {
  offset.value += direction * pageSize.value;
  void load();
}
async function run(action: () => Promise<void>) {
  busy.value = true;
  error.value = notice.value = "";
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
function createAccount() {
  creationMounted.value = true;
  creationDialog.value?.showModal();
}
async function changed(message = "") {
  await load(true);
  emit("changed");
  notice.value = message;
}
async function manageLicense(user: any) {
  licenseUserId.value = user.id;
  await go("licenses");
}
async function saveSettings() {
  await run(async () => {
    await api("/system/settings", "PUT", settingsForm);
    await changed("站点名称已更新。");
  });
}
async function backup() {
  await run(async () => {
    await api("/system/backups", "POST");
    await load();
    notice.value = "备份已完成";
  });
}
async function download(name: string) {
  await run(async () => {
    const r = await fetch(
      `/api/system/backups/${encodeURIComponent(name)}/download`,
      { headers: { Authorization: `Bearer ${session.token}` } },
    );
    if (!r.ok) throw new Error("备份下载失败，请重试。");
    const url = URL.createObjectURL(await r.blob()),
      link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
}
defineExpose({ reload: () => load(true) });
</script>
<template>
  <div class="admin-shell">
    <button
      v-if="mobileMenu"
      class="admin-nav-scrim"
      aria-label="收起管理导航"
      @click="mobileMenu = false"
    ></button>
    <aside class="admin-sidebar" :class="{ expanded: mobileMenu }">
      <a
        class="admin-brand"
        href="#/admin/overview"
        @click.prevent="go('overview')"
        ><span><School :size="23" /></span
        ><strong>ClassGlow<small>学校管理控制台</small></strong></a
      >
      <div class="admin-school">
        <span class="school-emblem"><School :size="17" /></span>
        <div>
          <strong>{{ overview?.siteName ?? "班级看板" }}</strong
          ><small>学校管理空间</small>
        </div>
        <ShieldCheck :size="15" />
      </div>
      <nav aria-label="管理后台导航">
        <div v-for="group in groups" :key="group" class="admin-nav-group">
          <p>{{ group }}</p>
          <a
            v-for="item in sections.filter((s) => s.group === group)"
            :key="item.id"
            :href="`#/admin/${item.id}`"
            :class="{ active: tab === item.id }"
            :aria-current="tab === item.id ? 'page' : undefined"
            @click.prevent="go(item.id)"
            ><component :is="item.icon" :size="18" /><span>{{ item.name }}</span
            ><ChevronRight v-if="tab === item.id" :size="14"
          /></a>
        </div>
      </nav>
      <div class="admin-sidebar-bottom">
        <button @click="emit('teaching')">
          <ArrowUpRight :size="17" />进入教学工作台
        </button>
        <div class="admin-profile">
          <span class="admin-avatar">{{ session.name.slice(0, 1) }}</span>
          <div>
            <strong>{{ session.name }}</strong
            ><small>系统管理员</small>
          </div>
          <button
            aria-label="退出管理员登录"
            title="退出登录"
            @click="emit('logout')"
          >
            <LogOut :size="17" />
          </button>
        </div>
      </div>
    </aside>
    <div class="admin-workspace">
      <header class="admin-topbar">
        <button
          class="icon-button admin-menu-toggle"
          aria-label="打开管理导航"
          :aria-expanded="mobileMenu"
          @click="mobileMenu = !mobileMenu"
        >
          <Menu :size="21" />
        </button>
        <div class="admin-breadcrumb">
          <span>管理后台</span><ChevronRight :size="14" /><strong>{{
            activeSection.name
          }}</strong>
        </div>
        <div class="admin-topbar-right">
          <span class="admin-secure"><ShieldCheck :size="14" />管理员空间</span
          ><span class="admin-avatar small">{{
            session.name.slice(0, 1)
          }}</span>
        </div>
      </header>
      <div class="admin-content">
        <header class="admin-page-heading">
          <div>
            <span class="admin-mini-label">{{ activeSection.group }}</span>
            <h1>{{ activeSection.name }}</h1>
          </div>
          <div class="admin-heading-actions">
            <button
              class="button secondary refresh-admin"
              :disabled="busy || loading"
              @click="refreshPage"
              aria-label="刷新管理数据"
            >
              <RefreshCw :size="16" :class="{ spinning: loading }" /><span
                >刷新</span
              ></button
            ><button
              v-if="['overview', 'users'].includes(tab)"
              class="button primary"
              @click="createAccount"
            >
              <Plus :size="17" />添加账号</button
            ><button
              v-if="tab === 'classes'"
              class="button primary"
              @click="emit('createClass')"
            >
              <Plus :size="17" />创建班级</button
            ><button
              v-if="tab === 'backups'"
              class="button primary"
              :disabled="busy || !backupSupported"
              @click="backup"
            >
              <Plus :size="17" />创建备份
            </button>
          </div>
        </header>
        <div v-if="error" class="admin-feedback error" role="alert">
          <span>{{ error }}</span
          ><button class="text-button" @click="load(true)">重新加载</button>
        </div>
        <div v-if="notice" class="admin-feedback success" role="status">
          <Check :size="17" />{{ notice
          }}<button
            class="icon-button"
            aria-label="关闭操作提示"
            @click="notice = ''"
          >
            <X :size="15" />
          </button>
        </div>
        <div v-if="!overview" class="admin-empty">
          <RefreshCw :size="25" :class="{ spinning: loading }" />
          <p>{{ loading ? "正在读取管理数据…" : "暂时无法加载，请重试。" }}</p>
        </div>
        <template v-else>
          <AdminOverview
            v-if="tab === 'overview'"
            :overview="overview"
            :activity="activity"
            @navigate="go"
            @create-account="createAccount"
            @create-class="emit('createClass')"
          />
          <LicenseManager
            v-else-if="tab === 'licenses'"
            ref="licenseView"
            :initial-user-id="licenseUserId"
            @changed="emit('changed')"
          />
          <div v-else-if="tab === 'settings'" class="admin-settings-grid">
            <section class="admin-card">
              <header class="admin-card-heading">
                <div>
                  <h2>基本信息</h2>
                </div>
                <School :size="20" />
              </header>
              <form class="admin-settings-form" @submit.prevent="saveSettings">
                <label class="field"
                  >学校 / 站点名称<input
                    v-model.trim="settingsForm.siteName"
                    required
                    maxlength="80" /></label
                ><label class="field"
                  >网站地址<input :value="overview.publicUrl" readonly
                /></label>
                <p class="admin-field-help">
                  更换域名请由服务器维护人员调整部署配置。
                </p>
                <button class="button primary" :disabled="busy">
                  {{ busy ? "正在保存…" : "保存设置" }}
                </button>
              </form>
            </section>
            <section class="admin-card">
              <header class="admin-card-heading">
                <div>
                  <h2>服务信息</h2>
                </div>
                <ShieldCheck :size="20" />
              </header>
              <dl class="admin-service-details">
                <div>
                  <dt>数据库</dt>
                  <dd>
                    {{
                      overview.database === "sqlite" ? "SQLite" : "PostgreSQL"
                    }}
                  </dd>
                </div>
                <div>
                  <dt>数据库大小</dt>
                  <dd>
                    {{
                      overview.databaseBytes === null
                        ? "由数据库服务管理"
                        : fileSize(overview.databaseBytes)
                    }}
                  </dd>
                </div>
                <div>
                  <dt>可用存储</dt>
                  <dd>{{ fileSize(overview.diskFreeBytes) }}</dd>
                </div>
                <div>
                  <dt>本次运行</dt>
                  <dd>{{ Math.floor(overview.uptimeSeconds / 60) }} 分钟</dd>
                </div>
                <div>
                  <dt>服务时间</dt>
                  <dd>{{ adminDate(overview.serverTime, true) }}</dd>
                </div>
              </dl>
            </section>
          </div>
          <section v-else-if="tab === 'backups'" class="admin-card">
            <div class="admin-backup-intro">
              <span class="metric-icon blue"><Database :size="22" /></span>
              <div>
                <h2>数据库备份</h2>
                <p>包含账号、班级、许可与教学内容。下载的备份请妥善保管。</p>
              </div>
              <span class="admin-pill muted">{{ rows.length }} 份备份</span>
            </div>
            <p v-if="!backupSupported" class="admin-feedback info">
              当前使用 PostgreSQL，请通过服务器菜单备份与恢复。
            </p>
            <div v-if="!rows.length" class="admin-empty">
              <Database :size="30" />
              <h3>还没有备份</h3>
              <button
                class="button secondary"
                :disabled="busy || !backupSupported"
                @click="backup"
              >
                创建第一份备份
              </button>
            </div>
            <div v-else class="admin-table-wrap">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th>备份文件</th>
                    <th>创建时间</th>
                    <th>大小</th>
                    <th>操作</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="item in rows" :key="item.name">
                    <td data-label="备份文件" class="primary-cell">
                      <Database :size="18" /><span class="file-name">{{
                        item.name
                      }}</span>
                    </td>
                    <td data-label="创建时间">
                      {{ adminDate(item.at, true) }}
                    </td>
                    <td data-label="大小">{{ fileSize(item.bytes) }}</td>
                    <td data-label="操作" class="table-actions">
                      <button @click="download(item.name)" :disabled="busy">
                        <Download :size="14" />下载</button
                      ><button
                        class="destructive"
                        :disabled="busy || !backupSupported"
                        @click="editDrawer?.open('restore', item)"
                      >
                        恢复
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="admin-tip">
              <ShieldCheck :size="17" />
              <p>
                恢复会替换全站数据，并让当前账号与设备退出登录。操作前系统会自动保留当前数据库副本。
              </p>
            </div>
          </section>
          <section
            v-else
            class="admin-card admin-list-card"
            :aria-busy="loading"
          >
            <div v-if="tab === 'users'" class="admin-list-tabs">
              <button
                :class="{ active: !filters.identity }"
                @click="
                  filters.identity = '';
                  filter();
                "
              >
                全部账号</button
              ><button
                v-for="r in [
                  ['headteacher', '班主任'],
                  ['teacher', '普通老师'],
                  ['system_admin', '系统管理员'],
                ]"
                :key="r[0]"
                :class="{ active: filters.identity === r[0] }"
                @click="
                  filters.identity = r[0]!;
                  filter();
                "
              >
                {{ r[1] }}
              </button>
            </div>
            <div class="admin-filterbar">
              <form class="admin-search" @submit.prevent="search">
                <Search :size="17" /><input
                  v-model="query"
                  :placeholder="
                    tab === 'users'
                      ? '搜索姓名或账号'
                      : tab === 'classes'
                        ? '搜索班级名称'
                        : tab === 'devices'
                          ? '搜索教室或设备名称'
                          : '搜索操作人或操作对象'
                  "
                  :aria-label="`搜索${activeSection.name}`"
                /><button type="submit">搜索</button>
              </form>
              <div class="admin-filter-selects">
                <SlidersHorizontal :size="16" /><select
                  v-if="tab !== 'audit'"
                  v-model="filters.status"
                  @change="filter"
                  :aria-label="`${activeSection.name}状态`"
                >
                  <option value="">全部状态</option>
                  <option v-for="s in statusOptions" :key="s[0]" :value="s[0]">
                    {{ s[1] }}
                  </option></select
                ><select
                  v-if="tab === 'users'"
                  v-model="filters.license"
                  @change="filter"
                  aria-label="高级许可状态"
                >
                  <option value="">全部许可</option>
                  <option value="active">许可有效</option>
                  <option value="expiring">7 天内到期</option>
                  <option value="expired">许可已到期</option>
                  <option value="free">未开通高级功能</option></select
                ><select
                  v-if="tab === 'audit'"
                  v-model="filters.action"
                  @change="filter"
                  aria-label="操作类型"
                >
                  <option value="">全部操作</option>
                  <option
                    v-for="(label, key) in auditNames"
                    :key="key"
                    :value="key"
                  >
                    {{ label }}
                  </option></select
                ><button
                  v-if="hasFilters"
                  class="text-button"
                  @click="resetFilters"
                >
                  重置
                </button>
              </div>
            </div>
            <div v-if="loading" class="admin-loading-line" role="status">
              正在加载…
            </div>
            <div v-if="!rows.length && !loading" class="admin-empty">
              <Inbox :size="32" />
              <h3>
                {{ hasFilters ? "没有找到匹配的记录" : "这里还没有记录" }}
              </h3>
              <p>
                {{
                  hasFilters
                    ? "试试其他关键词，或清除筛选条件。"
                    : tab === "users"
                      ? "添加老师账号，分配身份和任教班级。"
                      : tab === "classes"
                        ? "创建班级后，可以分配老师并绑定教室电脑。"
                        : tab === "devices"
                          ? "在教室电脑安装客户端，再由班主任绑定班级。"
                          : "管理操作完成后会自动记录在这里。"
                }}
              </p>
              <button
                v-if="hasFilters"
                class="button secondary"
                @click="resetFilters"
              >
                清除筛选</button
              ><button
                v-else-if="tab === 'users'"
                class="button primary"
                @click="createAccount"
              >
                添加第一个账号</button
              ><button
                v-else-if="tab === 'classes'"
                class="button primary"
                @click="emit('createClass')"
              >
                创建班级
              </button>
            </div>
            <AdminDataTable
              v-else
              :kind="tab"
              :rows="rows"
              @edit="(kind, item) => editDrawer?.open(kind, item)"
              @license="manageLicense"
              @open-class="emit('openClass', $event)"
            />
            <footer class="admin-pagination">
              <span
                >共 {{ total }} 条<span class="page-range" v-if="total">
                  · 显示 {{ offset + 1 }}–{{
                    Math.min(offset + pageSize, total)
                  }}</span
                ></span
              >
              <div>
                <select
                  v-model.number="pageSize"
                  @change="filter"
                  aria-label="每页条数"
                >
                  <option :value="10">10 条 / 页</option>
                  <option :value="20">20 条 / 页</option>
                  <option :value="50">50 条 / 页</option></select
                ><button
                  class="icon-button"
                  aria-label="上一页"
                  :disabled="loading || offset === 0"
                  @click="paginate(-1)"
                >
                  <ChevronLeft :size="17" /></button
                ><span class="page-number">{{
                  Math.floor(offset / pageSize) + 1
                }}</span
                ><button
                  class="icon-button"
                  aria-label="下一页"
                  :disabled="loading || offset + pageSize >= total"
                  @click="paginate(1)"
                >
                  <ChevronRight :size="17" />
                </button>
              </div>
            </footer>
          </section>
        </template>
        <footer class="admin-footer">
          <span>ClassGlow</span><span>北京时间 UTC+8</span>
        </footer>
      </div>
    </div>
    <dialog
      ref="creationDialog"
      class="admin-drawer creation-drawer"
      @cancel="creationBusy && $event.preventDefault()"
      @close="creationMounted = false"
    >
      <header>
        <div>
          <span class="admin-mini-label">账号管理</span>
          <h2>添加账号</h2>
        </div>
        <button
          class="icon-button"
          aria-label="关闭添加账号"
          :disabled="creationBusy"
          @click="creationDialog?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <div class="admin-drawer-body">
        <AccountOnboarding
          v-if="creationMounted"
          @working="creationBusy = $event"
          @changed="changed()"
        />
      </div>
    </dialog>
    <AdminEditDrawer
      ref="editDrawer"
      @changed="changed"
      @restored="emit('restored', $event)"
      @open-class="emit('openClass', $event)"
    />
  </div>
</template>
