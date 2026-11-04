<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import { Megaphone } from "@lucide/vue";
import { api, session } from "../api";
import { adminDate } from "../admin";
import AnnouncementMarkdown from "./AnnouncementMarkdown.vue";

const props = defineProps<{ paused?: boolean }>();
type Notice = {
  id: string;
  title: string;
  markdown: string;
  publishedAt: number;
};
const notice = ref<Notice | null>(null);
const dialog = ref<HTMLDialogElement>();
let mounted = false;
let checking = false;
let displayed = false;
let claimKey: string | undefined;
let timer: ReturnType<typeof setInterval>;

async function check() {
  if (
    !mounted ||
    checking ||
    displayed ||
    props.paused ||
    !session.token ||
    document.hidden ||
    document.querySelector("dialog[open]")
  )
    return;
  const token = session.token;
  checking = true;
  try {
    if (!notice.value) {
      claimKey ??= crypto.randomUUID();
      const result = await api<{ notice: Notice | null }>(
        "/announcements/next",
        "POST",
        undefined,
        claimKey,
      );
      if (!mounted || session.token !== token) return;
      if (result.notice === undefined) return;
      claimKey = undefined;
      notice.value = result.notice;
      await nextTick();
    }
    if (
      notice.value &&
      dialog.value &&
      mounted &&
      !props.paused &&
      session.token === token &&
      !document.hidden &&
      !document.querySelector("dialog[open]")
    ) {
      dialog.value.showModal();
      displayed = true;
    }
  } catch {
    // 临时断线在下一次检查时恢复，保留当前页面的正常操作。
  } finally {
    checking = false;
  }
}

function closed() {
  displayed = false;
  notice.value = null;
  void check();
}

watch(() => props.paused, check, { flush: "post" });
onMounted(() => {
  mounted = true;
  document.addEventListener("visibilitychange", check);
  document.addEventListener("close", check, true);
  timer = setInterval(check, 30000);
  void check();
});
onUnmounted(() => {
  mounted = false;
  clearInterval(timer);
  document.removeEventListener("visibilitychange", check);
  document.removeEventListener("close", check, true);
});
</script>

<template>
  <dialog
    v-if="notice"
    ref="dialog"
    class="modal notice-popup"
    aria-labelledby="notice-title"
    @close="closed"
  >
    <div class="notice-popup-meta">
      <Megaphone :size="16" />站点公告<span>{{
        adminDate(notice.publishedAt, true)
      }}</span>
    </div>
    <h2 id="notice-title">{{ notice.title }}</h2>
    <AnnouncementMarkdown
      class="notice-popup-content"
      :source="notice.markdown"
    />
    <div class="modal-actions">
      <button
        type="button"
        class="button primary"
        autofocus
        @click="dialog?.close()"
      >
        知道了
      </button>
    </div>
  </dialog>
</template>

<style scoped>
.notice-popup {
  width: min(610px, calc(100% - 28px));
  border-color: #e4e8ef;
  color: #1d1d1f;
}
.notice-popup h2 {
  font-size: 22px;
  overflow-wrap: anywhere;
}
.notice-popup-meta {
  display: flex;
  align-items: center;
  gap: 7px;
  flex-wrap: wrap;
  margin-bottom: 10px;
  color: #0068d9;
  font-size: 13px;
}
.notice-popup-meta span {
  margin-left: auto;
  color: #696976;
}
.notice-popup-content {
  max-height: 53dvh;
  overflow: auto;
  padding-right: 4px;
}
</style>
