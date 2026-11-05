<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { Database, Trash2, X } from "@lucide/vue";
import { api } from "../api";
import { adminDate, fileSize } from "../admin";
type Package = {
  name: string;
  bytes: number;
  at: number;
  protected: boolean;
};
type Report = {
  retentionDays: number;
  records: { label: string; count: number }[];
  backups: { count: number; bytes: number };
  downloads: Package[];
  downloadsSupported: boolean;
};
const report = ref<Report>();
const busy = ref(false);
const error = ref("");
const notice = ref("");
const dialog = ref<HTMLDialogElement>();
const pending = ref<{
  category: "records" | "backups" | "download";
  name: string;
  description: string;
}>();
const confirmation = ref("");
const total = computed(
  () => report.value?.records.reduce((sum, row) => sum + row.count, 0) ?? 0,
);
async function reload() {
  error.value = "";
  try {
    report.value = await api("/system/storage");
  } catch (e) {
    error.value = (e as Error).message;
  }
}
function prepare(category: "records" | "backups" | "download", name = "") {
  confirmation.value = error.value = "";
  pending.value = {
    category,
    name: category === "download" ? name : "清理过期数据",
    description:
      category === "download"
        ? `永久删除安装包 ${name}。`
        : category === "backups"
          ? "永久删除超过 30 天的历史备份，保留最近 3 份。"
          : "清理超过 30 天的日志、失效凭据和已关闭草稿正文。",
  };
  dialog.value?.showModal();
}
async function clean() {
  const request = pending.value;
  if (!request || busy.value || confirmation.value !== request.name) return;
  busy.value = true;
  error.value = notice.value = "";
  try {
    if (request.category === "download") {
      await api(
        `/system/storage/downloads/${encodeURIComponent(request.name)}`,
        "DELETE",
        { confirmation: request.name },
      );
    } else {
      await api("/system/storage/cleanup", "POST", {
        category: request.category,
        confirmation: confirmation.value,
      });
    }
    dialog.value?.close();
    notice.value = "清理完成。数据库释放的空间会在夜间维护时回收。";
    await reload();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
defineExpose({ reload });
onMounted(reload);
</script>
<template>
  <div class="storage-manager">
    <p v-if="error" class="admin-feedback error" role="alert">
      {{ error }}
    </p>
    <p v-if="notice" class="admin-feedback success" role="status">
      {{ notice }}
    </p>
    <template v-if="report">
      <section class="admin-card">
        <header class="admin-card-heading">
          <div>
            <h2>30 天自动清理</h2>
            <p>每天凌晨清理过期日志、失效记录及已关闭草稿正文。</p>
          </div>
          <Database :size="20" />
        </header>
        <div class="storage-body">
          <ul>
            <li v-for="row in report.records" :key="row.label">
              {{ row.label }} <strong>{{ row.count }} 条待清理</strong>
            </li>
          </ul>
          <button
            class="button secondary"
            :disabled="busy || !total"
            @click="prepare('records')"
          >
            立即清理过期记录
          </button>
        </div>
      </section>
      <section class="admin-card">
        <header class="admin-card-heading">
          <div>
            <h2>历史备份</h2>
            <p>超过 30 天的备份可批量删除，最近 3 份会保留。</p>
          </div>
        </header>
        <div class="storage-body">
          <p>
            {{ report.backups.count }} 份可清理 ·
            {{ fileSize(report.backups.bytes) }}
          </p>
          <button
            class="button secondary"
            :disabled="busy || !report.backups.count"
            @click="prepare('backups')"
          >
            清理历史备份
          </button>
          <p class="muted small">
            逐份查看、下载或删除，请使用“数据备份”。
          </p>
        </div>
      </section>
      <section class="admin-card">
        <header class="admin-card-heading">
          <div>
            <h2>Windows 安装包</h2>
            <p>保留当前发布包、待发布版本及最近一个回退版本。</p>
          </div>
        </header>
        <p v-if="!report.downloadsSupported" class="storage-body muted">
          服务器尚未接入安装包目录。
        </p>
        <div v-else class="admin-table-wrap">
          <table class="admin-table">
            <thead>
              <tr>
                <th>安装包</th>
                <th>大小</th>
                <th>上传时间</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in report.downloads" :key="item.name">
                <td class="package-name">{{ item.name }}</td>
                <td>{{ fileSize(item.bytes) }}</td>
                <td>{{ adminDate(item.at) }}</td>
                <td>
                  <span v-if="item.protected" class="muted">保留</span
                  ><button
                    v-else
                    class="text-button destructive"
                    :disabled="busy"
                    @click="prepare('download', item.name)"
                  >
                    <Trash2 :size="14" />删除
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
          <p v-if="!report.downloads.length" class="storage-body muted">
            暂无安装包。
          </p>
        </div>
      </section>
    </template>
    <p v-else-if="!error" role="status">正在统计存储…</p>
    <dialog
      ref="dialog"
      class="admin-drawer"
      @cancel="busy && $event.preventDefault()"
    >
      <header>
        <h2>确认清理</h2>
        <button
          class="icon-button"
          aria-label="关闭清理确认"
          :disabled="busy"
          @click="dialog?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <form @submit.prevent="clean">
        <div class="admin-drawer-body">
          <p>{{ pending?.description }}</p>
          <p>删除后无法撤销。</p>
          <label class="field"
            >输入「{{ pending?.name }}」确认<input
              v-model="confirmation"
              required
              autocomplete="off"
              :disabled="busy"
          /></label>
          <p v-if="error" class="field-error" role="alert">{{ error }}</p>
        </div>
        <footer class="admin-drawer-footer">
          <button
            type="button"
            class="button secondary"
            :disabled="busy"
            @click="dialog?.close()"
          >
            取消</button
          ><button
            class="button danger-button"
            :disabled="busy || confirmation !== pending?.name"
          >
            {{ busy ? "正在清理…" : "确认清理" }}
          </button>
        </footer>
      </form>
    </dialog>
  </div>
</template>
<style scoped>
.storage-manager {
  display: grid;
  gap: 20px;
}
.storage-body {
  padding: 18px 24px;
}
.storage-body ul {
  padding: 0;
  list-style: none;
  margin: 0 0 20px;
}
.storage-body li {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 0;
}
.package-name {
  overflow-wrap: anywhere;
}
</style>
