<script setup lang="ts">
import { ref } from "vue";
import { Trash2, X } from "@lucide/vue";
import { api } from "../api";
type Kind = "users" | "classes" | "devices" | "announcements";
const emit = defineEmits<{ changed: [message: string] }>();
const dialog = ref<HTMLDialogElement>();
const target = ref<{ kind: Kind; id: string }>();
const details = ref<{
  name: string;
  blockedReason: string;
  counts: { label: string; count: number }[];
}>();
const confirmation = ref("");
const error = ref("");
const busy = ref(false);
const scopes: Record<Kind, string> = {
  users:
    "将删除账号、个人草稿、该账号发布的教学内容与历史、登录凭据及许可记录。负责的班级须先转移或删除。",
  classes:
    "将删除班级课表、画布、教学内容与历史、成员关系和设备记录。多班级草稿会移除本班，只关联本班的草稿将清空。",
  devices: "将删除这台已解绑电脑的记录、画面回传、配对记录和操作历史。",
  announcements: "将删除公告正文、接收范围和领取记录。",
};
async function open(kind: Kind, id: string) {
  if (busy.value) return;
  target.value = { kind, id };
  details.value = undefined;
  confirmation.value = error.value = "";
  busy.value = true;
  dialog.value?.showModal();
  try {
    details.value = await api(
      `/system/deletion/${kind}/${encodeURIComponent(id)}`,
    );
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function remove() {
  if (
    !target.value ||
    !details.value ||
    busy.value ||
    details.value.blockedReason ||
    confirmation.value !== details.value.name
  )
    return;
  busy.value = true;
  error.value = "";
  try {
    await api(
      `/system/deletion/${target.value.kind}/${encodeURIComponent(target.value.id)}`,
      "DELETE",
      { confirmation: confirmation.value },
    );
    dialog.value?.close();
    emit("changed", `已永久删除「${details.value.name}」。`);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
defineExpose({ open });
</script>
<template>
  <dialog
    ref="dialog"
    class="admin-drawer"
    @cancel="busy && $event.preventDefault()"
  >
    <header>
      <h2>永久删除</h2>
      <button
        class="icon-button"
        aria-label="关闭删除确认"
        :disabled="busy"
        @click="dialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <form @submit.prevent="remove">
      <div class="admin-drawer-body">
        <p v-if="!details && busy" role="status">正在核对影响范围…</p>
        <template v-if="details && target">
          <div class="admin-danger-box">
            <Trash2 :size="24" />
            <h3>{{ details.name }}</h3>
            <p>{{ scopes[target.kind] }}</p>
          </div>
          <ul>
            <li v-for="row in details.counts" :key="row.label">
              {{ row.label }} · {{ row.count }} 条
            </li>
          </ul>
          <p>删除后无法撤销。历史备份中的副本可在“数据备份”中单独删除。</p>
          <p v-if="details.blockedReason" class="field-error" role="alert">
            {{ details.blockedReason }}
          </p>
          <label v-else class="field"
            >输入「{{ details.name }}」确认<input
              v-model="confirmation"
              required
              autocomplete="off"
              :disabled="busy"
          /></label>
        </template>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      </div>
      <footer class="admin-drawer-footer">
        <button
          type="button"
          class="button secondary"
          :disabled="busy"
          @click="dialog?.close()"
        >
          取消
        </button>
        <button
          class="button danger-button"
          :disabled="
            busy ||
            !details ||
            !!details.blockedReason ||
            confirmation !== details.name
          "
        >
          {{ busy ? "正在处理…" : "永久删除" }}
        </button>
      </footer>
    </form>
  </dialog>
</template>
