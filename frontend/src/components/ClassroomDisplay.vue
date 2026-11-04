<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from "vue";
import { Maximize2, Monitor, RefreshCw, X } from "@lucide/vue";
import { api, session } from "../api";
import { model } from "../store";
import { adminDate } from "../admin";
import type { Device } from "../domain";
const props = defineProps<{ device: Device; reload: () => Promise<void> }>();
type Display = {
  requestId: string;
  requestedAt: number;
  receivedAt: number;
  pending: boolean;
  image?: string;
  classVersion: number;
  device?: Device;
  report: {
    requestId?: string;
    availability?: string;
    appliedVersion?: number;
    renderedAt?: number;
    page?: number;
    pageCount?: number;
    applyState?: string;
  };
};
const data = ref<Display>(),
  image = ref(""),
  error = ref(""),
  busy = ref(false),
  enlarged = ref<HTMLDialogElement>();
const now = ref(Date.now() + session.clockOffset),
  checking = ref(false);
let generation = 0,
  reading = false;
const supported = computed(() => {
  const version = (props.device.clientVersion || "0.0.0")
    .split(".")
    .map(Number);
  return (
    version[0]! > 0 || version[1]! > 4 || (version[1] === 4 && version[2]! >= 6)
  );
});
const waiting = computed(
  () =>
    props.device.online &&
    data.value?.pending &&
    data.value.requestedAt * 1000 > now.value - 90000,
);
const stateLabel = computed(() => {
  if (!props.device.online) return "电脑离线 · 保留最近回传";
  if (waiting.value) return "正在向教室电脑获取画面…";
  if (
    data.value?.requestId &&
    data.value.report.requestId !== data.value.requestId
  )
    return "暂未收到回传，请稍后刷新";
  if (props.device.localPaused || props.device.remotePaused)
    return "教室看板已暂停";
  if (props.device.error) return props.device.error;
  if (props.device.deferred) return "已接收更新，等待全屏展示结束";
  if (data.value?.report.availability === "disabled")
    return "教室电脑尚未启用展示";
  if (data.value?.report.availability === "replaced")
    return "本机壁纸已更换，当前未展示看板";
  if (!image.value)
    return supported.value ? "尚无看板画面" : "升级客户端后可查看画面";
  if (data.value!.report.appliedVersion! < data.value!.classVersion)
    return "有新的发布内容，电脑画面尚未同步";
  return "最近回传的看板";
});
async function read(serial: number) {
  const result = await api<Display>(
    `/devices/${props.device.id}/display?after=${data.value?.receivedAt || 0}`,
  );
  if (serial !== generation) return;
  data.value = result;
  if (result.image !== undefined) image.value = result.image;
  const index = model.devices.findIndex((d) => d.id === result.device?.id);
  if (index >= 0 && result.device) model.devices[index] = result.device;
}
async function refresh() {
  if (busy.value) return;
  const serial = generation;
  busy.value = true;
  error.value = "";
  try {
    if (props.device.online && supported.value) {
      await api(`/devices/${props.device.id}/display-request`, "POST");
    }
    if (serial !== generation) return;
    await read(serial);
  } catch (e) {
    if (serial === generation) error.value = (e as Error).message;
  } finally {
    if (serial === generation) busy.value = false;
  }
}
watch(
  () => props.device.id,
  async () => {
    ++generation;
    data.value = undefined;
    image.value = "";
    error.value = "";
    busy.value = false;
    enlarged.value?.close();
    await refresh();
  },
  { immediate: true },
);
const timer = window.setInterval(async () => {
  now.value = Date.now() + session.clockOffset;
  if (
    !waiting.value ||
    busy.value ||
    reading ||
    document.visibilityState !== "visible"
  )
    return;
  reading = true;
  checking.value = true;
  try {
    await read(generation);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    reading = false;
    checking.value = false;
  }
}, 1000);
onUnmounted(() => {
  ++generation;
  clearInterval(timer);
});
</script>

<template>
  <section
    class="panel classroom-display"
    aria-labelledby="classroom-display-title"
  >
    <header>
      <div>
        <span class="display-eyebrow">教室电脑回传</span>
        <h2 id="classroom-display-title">{{ device.name }}的画面</h2>
      </div>
      <button
        class="button primary"
        :disabled="busy || !!waiting"
        @click="refresh"
      >
        <RefreshCw :size="16" :class="{ spinning: busy || checking }" />{{
          waiting ? "等待电脑回传" : "刷新画面"
        }}
      </button>
    </header>
    <div class="display-status" :class="{ pending: waiting }" role="status">
      <span
        class="status-dot"
        :class="{
          amber:
            !device.online ||
            waiting ||
            device.applied < (data?.classVersion || 0),
        }"
      ></span
      >{{ stateLabel }}
    </div>
    <p v-if="error" role="alert" class="notice danger">{{ error }}</p>
    <div class="display-frame">
      <img
        v-if="image"
        :src="image"
        :alt="`${device.name}最近回传的班级看板，第${data?.report.page}页`"
      />
      <div v-else class="display-placeholder">
        <Monitor :size="38" /><strong>{{ stateLabel }}</strong
        ><span v-if="!supported">需要 Windows 0.4.6 或更新版本</span
        ><span v-else-if="waiting">正在读取这台电脑已显示的看板</span
        ><span v-else>在教室电脑启用展示后，点击刷新画面。</span
        ><a v-if="!supported" href="/download" target="_blank" rel="noopener"
          >下载新版客户端</a
        >
      </div>
      <button
        v-if="image"
        class="display-zoom"
        aria-label="放大看板画面"
        @click="enlarged?.showModal()"
      >
        <Maximize2 :size="16" />放大
      </button>
    </div>
    <footer>
      <span v-if="data?.receivedAt"
        >回传于 {{ adminDate(data.receivedAt, true) }} · 第
        {{ data.report.page }} / {{ data.report.pageCount }} 页</span
      ><span v-else>尚未收到画面</span
      ><span v-if="data?.report.appliedVersion"
        >电脑 v{{ data.report.appliedVersion }} · 最新发布 v{{
          data.classVersion
        }}</span
      >
    </footer>
    <p class="display-caption">
      画面来自看板程序，不包含课件或其他窗口。{{
        image ? "这是最近一次回传，点击刷新获取当前状态。" : ""
      }}
    </p>
    <dialog ref="enlarged" class="display-lightbox">
      <header>
        <strong>{{ device.name }} · 最近回传画面</strong
        ><button
          class="icon-button"
          aria-label="关闭放大画面"
          @click="enlarged?.close()"
        >
          <X :size="22" />
        </button>
      </header>
      <img v-if="image" :src="image" alt="教室电脑回传的完整看板" />
      <p>{{ stateLabel }} · {{ adminDate(data?.receivedAt || 0, true) }}</p>
    </dialog>
  </section>
</template>

<style scoped>
.classroom-display {
  margin: 18px 0;
}
.classroom-display > header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.display-eyebrow {
  color: #8996a6;
  font-size: 11px;
  letter-spacing: 0.08em;
}
.classroom-display h2 {
  margin: 5px 0 0;
  font-size: 20px;
}
.display-status {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: #627388;
  margin: 18px 0 12px;
}
.display-frame {
  position: relative;
  display: grid;
  place-items: center;
  width: 100%;
  aspect-ratio: 16/9;
  max-height: 560px;
  overflow: hidden;
  border: 1px solid #e5eaf2;
  border-radius: 14px;
  background: #eef1f5;
}
.display-frame > img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  min-height: 0;
}
.display-placeholder {
  display: grid;
  justify-items: center;
  gap: 12px;
  padding: 24px;
  color: #8895a7;
  font-size: 12px;
  text-align: center;
}
.display-placeholder strong {
  color: #5b6c82;
  font-size: 15px;
}
.display-zoom {
  display: flex;
  gap: 7px;
  align-items: center;
  position: absolute;
  right: 14px;
  bottom: 14px;
  background: #ffffffea;
  backdrop-filter: blur(8px);
  border: 1px solid #ffffff;
  border-radius: 9px;
  padding: 9px 12px;
  color: #3e5474;
  cursor: pointer;
  box-shadow: 0 2px 12px #0001;
}
.classroom-display > footer {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
  color: #67798e;
  font-size: 12px;
  margin-top: 13px;
}
.display-caption {
  font-size: 11px;
  color: #98a2ae;
  margin: 8px 0 0;
}
.display-lightbox {
  width: min(1400px, calc(100vw - 28px));
  max-height: calc(100dvh - 28px);
  padding: 18px;
  border: 0;
  border-radius: 18px;
  background: #f7f9fc;
}
.display-lightbox::backdrop {
  background: #162333aa;
  backdrop-filter: blur(6px);
}
.display-lightbox header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.display-lightbox img {
  display: block;
  width: 100%;
  max-height: calc(100dvh - 155px);
  object-fit: contain;
}
.display-lightbox p {
  font-size: 12px;
  color: #718099;
  margin-bottom: 0;
}
@media (max-width: 600px) {
  .classroom-display > header {
    align-items: flex-start;
  }
  .classroom-display h2 {
    font-size: 18px;
  }
  .display-frame {
    min-height: 180px;
  }
  .classroom-display .button {
    font-size: 12px;
    padding-inline: 10px;
  }
}
</style>
