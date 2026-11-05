<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { Megaphone, Search, Send, X } from "@lucide/vue";
import { api } from "../api";
import { adminDate } from "../admin";
import AnnouncementMarkdown from "./AnnouncementMarkdown.vue";
import PermanentDelete from "./PermanentDelete.vue";
const deletion = ref<InstanceType<typeof PermanentDelete>>();

type Account = { id: string; name: string; login: string };
type Announcement = {
  id: string;
  title: string;
  markdown: string;
  scope: "all" | "selected";
  publishedAt: number;
  withdrawnAt: number | null;
  targetCount: number | null;
  shownCount: number;
};
type Page<T> = { items: T[]; total: number };

const form = reactive({
  title: "",
  markdown: "",
  scope: "all" as Announcement["scope"],
});
const accounts = ref<Account[]>([]);
const selected = ref<Account[]>([]);
const search = ref("");
const accountQuery = ref("");
const accountOffset = ref(0);
const accountTotal = ref(0);
const accountBusy = ref(false);
const accountError = ref("");
const rows = ref<Announcement[]>([]);
const total = ref(0);
const offset = ref(0);
const historyBusy = ref(false);
const historyError = ref("");
const busy = ref(false);
const error = ref("");
const notice = ref("");
const pageSize = 20;
let request: { payload: string; key: string } | undefined;
const ids = computed(
  () => new Set(selected.value.map((account) => account.id)),
);
const scopeLabel = computed(() =>
  form.scope === "all" ? "全体账号" : selected.value.length + " 个账号",
);
const canPublish = computed(
  () =>
    !!form.title.trim() &&
    !!form.markdown.trim() &&
    (form.scope === "all" || selected.value.length > 0),
);

async function searchAccounts(next = 0, query = search.value.trim()) {
  if (accountBusy.value) return;
  accountBusy.value = true;
  accountError.value = "";
  try {
    const params = new URLSearchParams({
      search: query,
      offset: String(next),
      limit: String(pageSize),
    });
    const result = await api<Page<Account>>("/system/users?" + params);
    accounts.value = result.items;
    accountTotal.value = result.total;
    accountOffset.value = next;
    accountQuery.value = query;
  } catch (e) {
    accountError.value = (e as Error).message;
  } finally {
    accountBusy.value = false;
  }
}
watch(
  () => form.scope,
  (scope) => {
    if (scope === "selected" && !accounts.value.length)
      void searchAccounts();
  },
);
function toggleAccount(account: Account) {
  if (ids.value.has(account.id))
    selected.value = selected.value.filter(
      (entry) => entry.id !== account.id,
    );
  else if (selected.value.length < 500)
    selected.value = [...selected.value, account];
}

async function reload(next = offset.value) {
  if (historyBusy.value) return;
  historyBusy.value = true;
  historyError.value = "";
  try {
    const result = await api<Page<Announcement>>(
      "/system/announcements?offset=" + next + "&limit=" + pageSize,
    );
    rows.value = result.items;
    total.value = result.total;
    offset.value = next;
  } catch (e) {
    historyError.value = (e as Error).message;
  } finally {
    historyBusy.value = false;
  }
}
async function publish() {
  if (busy.value || historyBusy.value || !canPublish.value) return;
  const payload = {
    title: form.title.trim(),
    markdown: form.markdown,
    scope: form.scope,
    accountIds: form.scope === "selected" ? [...ids.value].sort() : [],
  };
  const serialized = JSON.stringify(payload);
  if (request?.payload !== serialized)
    request = { payload: serialized, key: crypto.randomUUID() };
  busy.value = true;
  error.value = notice.value = "";
  try {
    await api("/system/announcements", "POST", payload, request.key);
    notice.value = "公告已发布给" + scopeLabel.value + "。";
    Object.assign(form, { title: "", markdown: "", scope: "all" });
    selected.value = [];
    request = undefined;
    await reload(0);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function withdraw(id: string) {
  if (busy.value || historyBusy.value) return;
  busy.value = true;
  error.value = notice.value = "";
  try {
    await api("/system/announcements/" + id + "/withdraw", "POST");
    notice.value = "公告已撤回。";
    await reload();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}

defineExpose({ reload });
onMounted(() => void reload());
</script>

<template>
  <div class="announcement-admin">
    <p v-if="error" class="admin-feedback error" role="alert">
      {{ error }}
    </p>
    <p v-if="notice" class="admin-feedback success" role="status">
      {{ notice }}
    </p>
    <div class="announcement-admin-grid">
      <section class="admin-card">
        <header class="admin-card-heading">
          <div>
            <h2>新建公告</h2>
            <p>每条公告对同一账号弹出一次</p>
          </div>
          <Megaphone :size="19" />
        </header>
        <form @submit.prevent="publish">
          <fieldset :disabled="busy">
            <label class="field"
              >标题<input
                v-model="form.title"
                required
                maxlength="120"
                placeholder="公告标题"
            /></label>
            <label class="field"
              >正文（Markdown）
              <textarea
                v-model="form.markdown"
                required
                maxlength="16000"
                rows="8"
                placeholder="支持标题、列表、加粗、表格和链接"
              />
            </label>
            <label class="field"
              >接收范围
              <AppSelect v-model="form.scope">
                <option value="all">全体账号</option>
                <option value="selected">指定账号</option>
              </AppSelect>
            </label>
            <p
              v-if="form.scope === 'all'"
              class="announcement-scope-note muted small"
            >
              公告撤回前，后续开通的账号也会收到。
            </p>
            <div
              v-if="form.scope === 'selected'"
              class="announcement-targets"
            >
              <div class="announcement-account-search">
                <input
                  v-model="search"
                  aria-label="搜索接收账号"
                  placeholder="按姓名或账号搜索"
                  :disabled="accountBusy"
                  @keydown.enter.prevent="searchAccounts()"
                />
                <button
                  type="button"
                  class="button secondary"
                  :disabled="accountBusy"
                  @click="searchAccounts()"
                >
                  <Search :size="15" />搜索
                </button>
              </div>
              <p v-if="accountError" class="field-error" role="alert">
                {{ accountError }}
              </p>
              <div
                v-if="selected.length"
                class="announcement-selected"
                aria-label="已选接收账号"
              >
                <button
                  v-for="account in selected"
                  :key="account.id"
                  type="button"
                  class="text-button"
                  :aria-label="'移除 ' + account.name + ' ' + account.login"
                  @click="toggleAccount(account)"
                >
                  {{ account.name }}<X :size="12" />
                </button>
              </div>
              <p class="muted small" role="status">
                已选 {{ selected.length }} 个账号
              </p>
              <div class="announcement-account-list">
                <p v-if="accountBusy" class="muted small">正在读取账号…</p>
                <p v-else-if="!accounts.length" class="muted small">
                  没有匹配的账号
                </p>
                <label v-for="account in accounts" :key="account.id">
                  <input
                    type="checkbox"
                    :checked="ids.has(account.id)"
                    :disabled="
                      accountBusy ||
                      (selected.length >= 500 && !ids.has(account.id))
                    "
                    @change="toggleAccount(account)"
                  />
                  <span
                    >{{ account.name }}
                    <small>{{ account.login }}</small></span
                  >
                </label>
              </div>
              <div
                v-if="accountTotal > pageSize"
                class="announcement-paging"
              >
                <button
                  type="button"
                  class="text-button"
                  :disabled="accountBusy || accountOffset === 0"
                  @click="
                    searchAccounts(accountOffset - pageSize, accountQuery)
                  "
                >
                  上一页
                </button>
                <span
                  >{{ accountOffset + 1 }}–{{
                    Math.min(accountOffset + pageSize, accountTotal)
                  }}
                  / {{ accountTotal }}</span
                >
                <button
                  type="button"
                  class="text-button"
                  :disabled="
                    accountBusy || accountOffset + pageSize >= accountTotal
                  "
                  @click="
                    searchAccounts(accountOffset + pageSize, accountQuery)
                  "
                >
                  下一页
                </button>
              </div>
            </div>
            <button
              type="submit"
              class="button primary announcement-publish"
              :disabled="busy || historyBusy || !canPublish"
            >
              <Send :size="16" />{{
                busy ? "正在处理…" : "发布给" + scopeLabel
              }}
            </button>
          </fieldset>
        </form>
      </section>
      <section class="admin-card">
        <header class="admin-card-heading">
          <div>
            <h2>弹窗预览</h2>
            <p>{{ scopeLabel }}</p>
          </div>
        </header>
        <div class="announcement-preview">
          <span>站点公告</span>
          <h3>{{ form.title.trim() || "公告标题" }}</h3>
          <AnnouncementMarkdown
            v-if="form.markdown.trim()"
            :source="form.markdown"
          />
          <p v-else class="muted">填写正文后在这里预览。</p>
        </div>
      </section>
    </div>
    <section class="admin-card">
      <header class="admin-card-heading">
        <div>
          <h2>发布记录</h2>
          <p>共 {{ total }} 条</p>
        </div>
      </header>
      <p v-if="historyError" class="field-error" role="alert">
        {{ historyError }}
      </p>
      <p v-else-if="historyBusy" class="muted">正在读取公告…</p>
      <p v-else-if="!rows.length" class="muted">暂无已发布公告</p>
      <article
        v-for="row in rows"
        :key="row.id"
        class="announcement-history-row"
      >
        <details>
          <summary>{{ row.title }}</summary>
          <AnnouncementMarkdown :source="row.markdown" />
        </details>
        <div class="announcement-history-meta">
          <span>{{ adminDate(row.publishedAt, true) }}</span>
          <span>{{
            row.scope === "all"
              ? "全体账号"
              : "指定 " + row.targetCount + " 个账号"
          }}</span>
          <span>已弹出 {{ row.shownCount }} 人</span>
          <span v-if="row.withdrawnAt" class="muted">已撤回</span>
          <button
            v-else
            type="button"
            class="text-button"
            :disabled="busy || historyBusy"
            @click="withdraw(row.id)"
          >
            撤回
          </button>
          <button
            v-if="row.withdrawnAt"
            class="text-button destructive"
            :disabled="busy || historyBusy"
            @click="deletion?.open('announcements', row.id)"
          >
            永久删除
          </button>
        </div>
      </article>
      <div v-if="total > pageSize" class="announcement-paging">
        <button
          type="button"
          class="text-button"
          :disabled="historyBusy || busy || offset === 0"
          @click="reload(offset - pageSize)"
        >
          上一页
        </button>
        <span
          >{{ offset + 1 }}–{{ Math.min(offset + pageSize, total) }} /
          {{ total }}</span
        >
        <button
          type="button"
          class="text-button"
          :disabled="historyBusy || busy || offset + pageSize >= total"
          @click="reload(offset + pageSize)"
        >
          下一页
        </button>
      </div>
    </section>
  </div>
  <PermanentDelete
    ref="deletion"
    @changed="
      notice = $event;
      reload(0);
    "
  />
</template>

<style scoped>
.announcement-admin,
.announcement-admin-grid {
  display: grid;
  gap: 18px;
}
.announcement-admin-grid {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  align-items: start;
}
.announcement-admin .admin-card {
  padding-bottom: 22px;
}
.announcement-admin .admin-card > :not(header) {
  margin-inline: 22px;
}
.announcement-scope-note {
  margin-top: 7px;
}
.announcement-preview {
  border: 1px solid #e5e8ef;
  border-radius: 14px;
  padding: 18px;
  max-height: 65vh;
  overflow: auto;
  overflow-wrap: anywhere;
}
.announcement-preview > span {
  font-size: 12px;
  color: #0068d9;
}
.announcement-preview > h3 {
  font-size: 20px;
  margin: 6px 0 12px;
}
.announcement-targets,
.announcement-publish {
  margin-top: 14px;
}
.announcement-account-search,
.announcement-history-meta,
.announcement-selected,
.announcement-paging {
  display: flex;
  gap: 8px 14px;
  align-items: center;
}
.announcement-account-search > input {
  min-width: 0;
}
.announcement-account-search > button {
  flex-shrink: 0;
}
.announcement-selected,
.announcement-history-meta {
  flex-wrap: wrap;
}
.announcement-selected > button {
  background: #edf4ff;
  border-radius: 8px;
  padding: 3px 7px;
}
.announcement-account-list {
  max-height: 205px;
  overflow: auto;
  border: 1px solid #e5e8ef;
  border-radius: 10px;
  padding: 6px 10px;
}
.announcement-account-list label {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 6px 0;
}
.announcement-account-list input {
  width: 16px;
  min-height: 16px;
}
.announcement-account-list small,
.announcement-history-meta {
  color: #686c78;
  font-size: 12px;
}
.announcement-history-row {
  padding: 12px 0;
  border-top: 1px solid #e5e8ef;
}
.announcement-history-row summary {
  cursor: pointer;
  overflow-wrap: anywhere;
  font-weight: 600;
}
.announcement-history-row details[open] .announcement-markdown {
  margin: 10px 0;
}
.announcement-paging {
  margin-top: 8px;
  font-size: 12px;
}
@media (max-width: 900px) {
  .announcement-admin-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
