<script setup lang="ts">
import { computed, reactive } from "vue";
import { Megaphone, Send, Square } from "@lucide/vue";
import type { Device } from "../domain";
const props = defineProps<{
  device: Device;
  disabled: boolean;
  stopDisabled: boolean;
  now: number;
}>();
const emit = defineEmits<{ send: [notice: ScreenNoticeInput]; stop: [] }>();
const form = reactive<ScreenNoticeInput>({
  mode: "marquee",
  message: "请同学们整理桌面，准备上课。",
  duration: 30,
  tone: "blue",
  overFullscreen: false,
});
const modes = [
  { id: "marquee", name: "跑马灯", caption: "顶部滚动" },
  { id: "barrage", name: "弹幕", caption: "悬浮飘过" },
  { id: "popup", name: "全屏弹窗", caption: "集中提醒" },
  { id: "countdown", name: "倒计时", caption: "限时任务" },
] as const;
const active = computed(
  () =>
    !!props.device.activeNoticeId &&
    (props.device.noticeEndsAt ?? 0) * 1000 + 5000 > props.now,
);
const valid = computed(
  () =>
    form.message.trim().length > 0 &&
    Number.isInteger(form.duration) &&
    form.duration >= 5 &&
    form.duration <= 3600,
);
</script>
<script lang="ts">
export interface ScreenNoticeInput {
  mode: "marquee" | "barrage" | "popup" | "countdown";
  message: string;
  duration: number;
  tone: "blue" | "amber" | "red";
  overFullscreen: boolean;
}
</script>
<template>
  <section class="panel screen-notice-controls">
    <header class="section-heading">
      <h2><Megaphone :size="20" />醒目提醒</h2>
      <span v-if="active" class="notice-playing" role="status"
        >这台电脑正在播放</span
      >
    </header>
    <form
      @submit.prevent="
        valid && emit('send', { ...form, message: form.message.trim() })
      "
    >
      <fieldset :disabled="disabled">
        <div class="notice-modes" role="group" aria-label="提醒形式">
          <button
            v-for="mode in modes"
            :key="mode.id"
            type="button"
            :aria-pressed="form.mode === mode.id"
            @click="form.mode = mode.id"
          >
            <strong>{{ mode.name }}</strong
            ><small>{{ mode.caption }}</small>
          </button>
        </div>
        <div class="notice-layout">
          <div class="notice-inputs">
            <label class="field"
              >提醒内容<textarea
                v-model="form.message"
                maxlength="160"
                rows="3"
                required
                placeholder="输入要告诉全班的话"
              />
            </label>
            <div class="notice-options">
              <label class="field"
                >{{
                  form.mode === "countdown" ? "倒计时（秒）" : "播放时长（秒）"
                }}<input
                  v-model.number="form.duration"
                  type="number"
                  min="5"
                  max="3600"
                  step="1"
                  required /></label
              ><label class="field"
                >颜色<AppSelect v-model="form.tone"
                  ><option value="blue">晴空蓝</option>
                  <option value="amber">暖金色</option>
                  <option value="red">醒目红</option></AppSelect
                ></label
              >
            </div>
            <label class="notice-override"
              ><input
                v-model="form.overFullscreen"
                type="checkbox"
              />全屏课件播放时也显示这条提醒</label
            >
          </div>
          <div
            class="notice-preview"
            :class="[`notice-mode-${form.mode}`, `notice-tone-${form.tone}`]"
            aria-label="醒目提醒效果预览"
          >
            <span class="notice-preview-label">效果预览</span>
            <div class="notice-preview-message">
              {{ form.message || "在这里写下提醒" }}
            </div>
            <strong
              v-if="form.mode === 'countdown'"
              class="notice-preview-count"
              >{{
                String(Math.floor((form.duration || 0) / 60)).padStart(2, "0")
              }}:{{
                String((form.duration || 0) % 60).padStart(2, "0")
              }}</strong
            >
          </div>
        </div>
        <p class="muted small">
          仅发给「{{
            device.name
          }}」。到时自动关闭，新的提醒会替换这台电脑上的旧提醒。倒计时结束后显示“时间到”5
          秒。
        </p>
        <button class="button primary" :disabled="disabled || !valid">
          <Send :size="16" />发送到这台电脑
        </button>
      </fieldset>
      <button
        type="button"
        class="button secondary stop-notice"
        :disabled="stopDisabled"
        @click="emit('stop')"
      >
        <Square :size="14" />停止这台电脑的提醒
      </button>
      <p v-if="disabled" class="muted small">
        需要班主任权限、高级许可及在线的 0.4.12
        或更新客户端。暂停展示时不能发送。
      </p>
    </form>
  </section>
</template>
<style scoped>
.screen-notice-controls {
  margin-top: 20px;
}
.screen-notice-controls h2 {
  display: flex;
  align-items: center;
  gap: 9px;
}
.screen-notice-controls fieldset {
  padding: 0;
  border: 0;
  margin: 0;
  min-width: 0;
}
.notice-modes {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
  margin: 14px 0 20px;
}
.notice-modes button {
  display: grid;
  gap: 5px;
  padding: 14px 8px;
  background: #f6f8fc;
  color: #354966;
  border: 1px solid transparent;
  border-radius: 14px;
  cursor: pointer;
}
.notice-modes button[aria-pressed="true"] {
  border-color: #6494f3;
  color: #2460c8;
  background: #edf3ff;
}
.notice-modes small {
  opacity: 0.7;
  font-size: 11px;
}
.notice-layout {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 22px;
}
.notice-inputs {
  min-width: 0;
}
.notice-options {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.notice-override {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 12px;
  line-height: 1.6;
}
.notice-override input {
  width: 16px;
  height: 16px;
}
.notice-preview {
  --tone: #17458c;
  position: relative;
  align-self: center;
  background: linear-gradient(135deg, #e5eef6, #ced9e7);
  aspect-ratio: 16/10;
  border-radius: 16px;
  overflow: hidden;
  padding: 42px 20px 20px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  text-align: center;
}
.notice-tone-red {
  --tone: #b72c43;
}
.notice-tone-amber {
  --tone: #916013;
}
.notice-preview-label {
  position: absolute;
  top: 12px;
  left: 14px;
  font-size: 10px;
  opacity: 0.65;
}
.notice-preview-message {
  overflow-wrap: anywhere;
  font-size: 18px;
  line-height: 1.6;
  color: var(--tone);
}
.notice-preview-count {
  font-size: 44px;
  color: var(--tone);
  margin-top: 12px;
  font-variant-numeric: tabular-nums;
}
.notice-mode-popup,
.notice-mode-countdown {
  background: var(--tone);
  color: white;
}
.notice-mode-popup .notice-preview-message,
.notice-mode-countdown .notice-preview-message,
.notice-mode-countdown .notice-preview-count {
  color: white;
}
.notice-mode-marquee .notice-preview-message,
.notice-mode-barrage .notice-preview-message {
  position: absolute;
  white-space: nowrap;
  top: 40px;
  left: 100%;
  padding: 8px 20px;
  background: var(--tone);
  color: white;
  animation: notice-flow 12s linear infinite;
}
.notice-mode-barrage .notice-preview-message {
  border-radius: 30px;
  top: 70px;
}
.notice-playing {
  font-size: 12px;
  color: #217356;
}
.stop-notice {
  margin-top: 12px;
}
@keyframes notice-flow {
  from {
    left: 100%;
    transform: translateX(0);
  }
  to {
    left: 0;
    transform: translateX(-100%);
  }
}
@media (max-width: 640px) {
  .notice-layout {
    grid-template-columns: 1fr;
  }
  .notice-preview {
    max-height: 230px;
  }
  .notice-modes {
    grid-template-columns: repeat(2, 1fr);
  }
}
@media (prefers-reduced-motion: reduce) {
  .notice-preview-message {
    animation: none !important;
    position: static !important;
    white-space: normal !important;
  }
}
</style>
