<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import AccessGate from "./components/AccessGate.vue";
import BoardDesigner from "./components/BoardDesigner.vue";
import AnnouncementPopup from "./components/AnnouncementPopup.vue";
import {
  api,
  ApiError,
  premiumAvailable,
  refreshModel,
  session,
  setSession,
} from "./api";
import { clone, model, replaceModel } from "./store";
import { classroomForDisplay } from "./classroomView";
import { isAdmin, layoutIssue, visible, type Layout } from "./domain";

const site = reactive({ initialized: false, siteName: "班级看板" });
const ready = ref(false),
  loading = ref(false),
  busy = ref(false),
  error = ref(""),
  notice = ref("");
const classId = new URLSearchParams(location.search).get("class");
const current = computed(() =>
  model.classes.find((c) => c.id === (classId ?? model.selected)),
);
const now = ref(Date.now());
const draft = ref<Layout>();
const confirmation = ref<HTMLDialogElement>();
const publishSummary = ref("");
const canEdit = computed(
  () =>
    !!current.value &&
    isAdmin(current.value) &&
    !current.value.archived &&
    premiumAvailable(now.value),
);
const previewClass = computed(() =>
  current.value ? classroomForDisplay(current.value, now.value) : undefined,
);
const contents = computed(() =>
  model.contents.filter(
    (c) => c.classId === current.value?.id && visible(c, now.value),
  ),
);
const draftKey = computed(
  () => `kanban-layout-input-${session.userId}-${current.value?.id}`,
);
let restoring = false;
let ticker: ReturnType<typeof setInterval>;

async function load() {
  loading.value = true;
  error.value = "";
  try {
    replaceModel(await refreshModel());
    if (!current.value || !isAdmin(current.value))
      throw new Error("你没有这个班级的看板设计权限。");
    document.title = `${current.value.name} · 看板画布`;
    restoring = true;
    draft.value = clone(current.value.layout);
    try {
      const saved = JSON.parse(localStorage.getItem(draftKey.value) ?? "null");
      if (saved && !layoutIssue(saved) && Number.isInteger(saved.revision)) {
        draft.value = saved;
        if (saved.revision !== current.value.layout.revision)
          notice.value = "草稿基于旧版看板，发布前请核对已发布的版本。";
      }
    } catch {
      /* 无有效本机草稿时使用已发布的看板。 */
    }
  } catch (e) {
    error.value = (e as Error).message;
    if (e instanceof ApiError && e.status === 401) {
      setSession("");
    }
  } finally {
    restoring = false;
    loading.value = false;
  }
}
async function signedIn(token: string) {
  setSession(token);

  await load();
}
watch(
  draft,
  () => {
    if (restoring || !draft.value || !canEdit.value) return;
    notice.value = "";
    try {
      localStorage.setItem(draftKey.value, JSON.stringify(draft.value));
    } catch {
      error.value = "草稿暂时无法保存，请保持画布页面打开。";
    }
  },
  { deep: true, flush: "sync" },
);
function confirmPublish(summary: string) {
  if (!draft.value || !canEdit.value || busy.value) return;
  error.value = layoutIssue(draft.value);
  if (error.value) return;
  publishSummary.value = summary;
  confirmation.value?.showModal();
}
async function publish() {
  if (!draft.value || !current.value || !canEdit.value || busy.value) return;
  busy.value = true;
  error.value = "";
  notice.value = "";
  try {
    // 保存接口返回新的布局修订，成功后直接使用，避免再读一次造成发布结果不明确。
    const result = await api<Layout>(
      `/classes/${current.value.id}/layout`,
      "PUT",
      clone(draft.value),
    );
    restoring = true;
    draft.value = clone(result);
    current.value.layout = clone(result);
    restoring = false;
    try {
      localStorage.removeItem(draftKey.value);
    } catch {
      /* 服务端发布结果已经确认。 */
    }
    notice.value = "已发布到教室。在线设备会自动同步。";
    confirmation.value?.close();
  } catch (e) {
    error.value = (e as Error).message;
    confirmation.value?.close();
  } finally {
    restoring = false;
    busy.value = false;
  }
}
onMounted(async () => {
  // 同源画布直接读取浏览器保存的会话，打开后解除 opener。
  window.opener = null;
  try {
    Object.assign(site, await api("/setup/status"));
    ready.value = true;
    if (session.token) await load();
  } catch (e) {
    ready.value = true;
    error.value = (e as Error).message;
  }
  ticker = setInterval(() => (now.value = Date.now()), 5000);
});
onUnmounted(() => clearInterval(ticker));
</script>
<template>
  <AnnouncementPopup
    v-if="ready && draft && !loading && session.token"
    :key="session.userId"
  />
  <main v-if="!ready || loading" class="studio-loading">
    <p>正在打开画布…</p>
  </main>
  <AccessGate
    v-else-if="!session.token"
    :site="site"
    @authenticated="signedIn"
  />
  <BoardDesigner
    v-else-if="draft && previewClass && isAdmin(previewClass)"
    v-model="draft"
    :classroom="previewClass"
    :contents="contents"
    :now="now"
    :disabled="!canEdit || busy"
    :disabled-reason="
      !canEdit ? '当前班级不可编辑，或高级许可已到期。' : undefined
    "
    :publish-error="error"
    :publish-notice="notice"
    standalone
    @publish="confirmPublish"
  />
  <main v-else class="studio-loading">
    <h1>暂时无法打开画布</h1>
    <p role="alert">{{ error || "请先选择你负责的班级。" }}</p>
    <a href="/">返回教师工作台</a>
  </main>
  <dialog
    ref="confirmation"
    class="modal studio-confirm"
    @cancel="busy && $event.preventDefault()"
  >
    <h2>发布到 {{ current?.name }}？</h2>
    <p>当前预览 · {{ publishSummary }}</p>
    <p>在线教室电脑会自动同步，实际页数以设备画面为准。</p>
    <div class="modal-actions">
      <button
        class="button secondary"
        :disabled="busy"
        @click="confirmation?.close()"
      >
        继续编辑</button
      ><button class="button primary" :disabled="busy" @click="publish">
        {{ busy ? "正在发布…" : "确认发布" }}
      </button>
    </div>
  </dialog>
</template>
<style scoped>
.studio-loading {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 20px;
  min-height: 100dvh;
  padding: 24px;
  text-align: center;
}
.studio-loading h1 {
  font-size: 22px;
}
.studio-confirm p {
  margin: 18px 0 24px;
  color: var(--muted);
  line-height: 1.8;
}
</style>
