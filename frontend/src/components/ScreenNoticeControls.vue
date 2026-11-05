<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import {
  Megaphone,
  Send,
  Square,
  SlidersHorizontal,
  Save,
  Trash2,
} from "@lucide/vue";
import type { Device } from "../domain";
import { session } from "../api";
import ScreenNoticePreview from "./ScreenNoticePreview.vue";
import {
  defaultNoticeStyle,
  noticeModes,
  noticePresets,
  readNoticeStyles,
  supportsNoticeDesign,
  validNoticeStyle,
  type ScreenNoticeInput,
  type SavedNoticeStyle,
  type NoticeMode,
  type NoticeStyle,
} from "../screenNotice";
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
const custom = ref(false),
  style = reactive(defaultNoticeStyle());
const templateName = ref(""),
  feedback = ref("");
const storageKey = `classglow-notice-styles-${session.userId}`;
const saved = ref<SavedNoticeStyle[]>([]);
try {
  saved.value = readNoticeStyles(localStorage.getItem(storageKey));
} catch {
  /* 浏览器禁用存储时仍可设计。 */
}
const payload = computed<ScreenNoticeInput>(() => ({
  ...form,
  message: form.message.trim(),
  ...(custom.value ? { style: { ...style } } : {}),
}));
const designedClient = computed(() =>
  supportsNoticeDesign(props.device.clientVersion),
);
const active = computed(
  () =>
    !!props.device.activeNoticeId &&
    (props.device.noticeEndsAt ?? 0) * 1000 + 5000 > props.now,
);
const scrolling = computed(() =>
  ["marquee", "barrage"].includes(form.mode),
);
const valid = computed(
  () =>
    form.message.trim().length > 0 &&
    form.message.length <= 160 &&
    Number.isInteger(form.duration) &&
    form.duration >= 5 &&
    form.duration <= 3600 &&
    (form.mode !== "barrage" ||
      form.message.split("\n").filter(Boolean).length <= 3) &&
    (!custom.value || validNoticeStyle(style)),
);
const sendDisabled = computed(
  () =>
    props.disabled ||
    !valid.value ||
    (custom.value && !designedClient.value),
);
function apply(preset: { mode: NoticeMode; style: NoticeStyle }) {
  form.mode = preset.mode;
  Object.assign(style, preset.style);
  custom.value = true;
  feedback.value = "";
}
function switchDesign(value: boolean) {
  custom.value = value;
}
function persist(next: SavedNoticeStyle[]) {
  try {
    localStorage.setItem(storageKey, JSON.stringify(next));
    saved.value = next;
    return true;
  } catch {
    feedback.value = "浏览器未能保存样式，请检查本地存储空间或浏览器设置。";
    return false;
  }
}
function saveStyle() {
  const name = templateName.value.trim();
  if (!name || name.length > 24 || !validNoticeStyle(style)) return;
  const previous = saved.value.find((s) => s.name === name);
  if (!previous && saved.value.length >= 12) {
    feedback.value = "最多保存 12 个样式，请先删除不再使用的样式。";
    return;
  }
  const next = saved.value.filter((s) => s.id !== previous?.id);
  next.push({
    id: previous?.id ?? crypto.randomUUID(),
    name,
    mode: form.mode,
    style: { ...style },
  });
  if (persist(next)) {
    feedback.value = previous
      ? "同名样式已更新。"
      : "样式已保存到当前浏览器。";
    templateName.value = "";
  }
}
function removeStyle(id: string) {
  if (persist(saved.value.filter((s) => s.id !== id)))
    feedback.value = "已删除保存的样式。";
}
function send() {
  if (!sendDisabled.value) emit("send", payload.value);
}
</script>
<script lang="ts">
export type { ScreenNoticeInput } from "../screenNotice";
</script>
<template>
  <section class="panel screen-notice-controls">
    <header class="section-heading">
      <h2><Megaphone :size="20" />醒目提醒</h2>
      <span v-if="active" class="notice-playing" role="status"
        >这台电脑正在播放</span
      >
    </header>
    <form @submit.prevent="send">
      <div class="notice-modes" role="group" aria-label="提醒形式">
        <button
          v-for="mode in noticeModes"
          :key="mode.id"
          type="button"
          :aria-pressed="form.mode === mode.id"
          @click="form.mode = mode.id"
        >
          <strong>{{ mode.name }}</strong
          ><small>{{ mode.caption }}</small>
        </button>
      </div>
      <div
        class="notice-design-tabs"
        role="group"
        aria-label="提醒设计方式"
      >
        <button
          type="button"
          :aria-pressed="!custom"
          @click="switchDesign(false)"
        >
          快捷配色
        </button>
        <button
          type="button"
          :aria-pressed="custom"
          @click="switchDesign(true)"
        >
          <SlidersHorizontal :size="15" />自定义设计
        </button>
      </div>
      <div v-if="custom" class="notice-presets" aria-label="设计起点">
        <span class="muted small">从样式开始</span
        ><button
          v-for="preset in noticePresets"
          :key="preset.name"
          type="button"
          @click="apply(preset)"
        >
          <i :style="{ background: preset.style.backgroundColor }"></i
          >{{ preset.name }}
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
          <p v-if="form.mode === 'barrage'" class="muted small">
            换行可分成最多 3 条弹幕。
          </p>
          <p
            v-if="
              form.mode === 'barrage' &&
              form.message.split('\n').filter(Boolean).length > 3
            "
            class="field-error"
            role="alert"
          >
            弹幕最多 3 条，请合并多余的行。
          </p>
          <div class="notice-options">
            <label class="field"
              >{{
                form.mode === "countdown"
                  ? "倒计时（秒）"
                  : "播放时长（秒）"
              }}<input
                v-model.number="form.duration"
                type="number"
                min="5"
                max="3600"
                step="1"
                required
            /></label>
            <label v-if="!custom" class="field"
              >颜色<AppSelect v-model="form.tone"
                ><option value="blue">晴空蓝</option>
                <option value="amber">暖金色</option>
                <option value="red">醒目红</option></AppSelect
              ></label
            >
            <label v-else class="field"
              >字体<AppSelect v-model="style.fontFamily"
                ><option value="sans">黑体</option>
                <option value="serif">宋体</option>
                <option value="kai">楷体</option></AppSelect
              ></label
            >
          </div>
          <template v-if="custom">
            <div class="notice-options">
              <label class="field"
                >字号<input
                  v-model.number="style.fontSize"
                  type="number"
                  min="24"
                  max="120"
                  step="1"
                  required
              /></label>
              <label class="field"
                >字重<AppSelect
                  :model-value="style.bold ? 'bold' : 'normal'"
                  @update:model-value="style.bold = $event === 'bold'"
                  ><option value="normal">常规</option>
                  <option value="bold">加粗</option></AppSelect
                ></label
              >
              <label class="field notice-color"
                >文字颜色<input
                  v-model="style.textColor"
                  type="color"
                /><span>{{ style.textColor }}</span></label
              >
              <label class="field notice-color"
                >背景颜色<input
                  v-model="style.backgroundColor"
                  type="color"
                /><span>{{ style.backgroundColor }}</span></label
              >
            </div>
            <label class="field notice-range"
              >背景不透明度 <output>{{ style.opacity }}%</output
              ><input
                v-model.number="style.opacity"
                type="range"
                min="20"
                max="100"
            /></label>
            <div class="notice-options">
              <label v-if="!scrolling" class="field"
                >呈现方式<AppSelect v-model="style.surface"
                  ><option value="fullscreen">铺满全屏</option>
                  <option value="card">悬浮卡片</option></AppSelect
                ></label
              >
              <label
                v-if="scrolling || style.surface === 'card'"
                class="field"
                >显示位置<AppSelect v-model="style.position"
                  ><option value="top">顶部</option>
                  <option value="center">中间</option>
                  <option value="bottom">底部</option></AppSelect
                ></label
              >
              <label v-if="scrolling" class="field"
                >滚动方向<AppSelect v-model="style.direction"
                  ><option value="left">从右向左</option>
                  <option value="right">从左向右</option></AppSelect
                ></label
              >
            </div>
            <label v-if="scrolling" class="field notice-range"
              >滚动一轮 <output>{{ style.flowSeconds }} 秒</output
              ><input
                v-model.number="style.flowSeconds"
                type="range"
                min="6"
                max="40"
              /><small class="muted">时间越长，滚动越慢。</small></label
            >
            <label v-if="!scrolling" class="field notice-range"
              >内容宽度 <output>{{ style.width }}%</output
              ><input
                v-model.number="style.width"
                type="range"
                min="40"
                max="90"
            /></label>
            <label
              v-if="
                form.mode === 'barrage' ||
                (!scrolling && style.surface === 'card')
              "
              class="field notice-range"
              >圆角 <output>{{ style.radius }}</output
              ><input
                v-model.number="style.radius"
                type="range"
                min="0"
                max="64"
            /></label>
          </template>
          <label class="notice-override"
            ><input
              v-model="form.overFullscreen"
              type="checkbox"
            />全屏课件播放时也显示这条提醒</label
          >
        </div>
        <div class="notice-preview-column">
          <ScreenNoticePreview :notice="payload" />
          <section v-if="custom" class="notice-saved">
            <h3>我的样式</h3>
            <p class="muted small">
              只保存外观，保存在当前账号的此浏览器中，最多 12
              个。同名保存会更新样式。
            </p>
            <div class="notice-save-row">
              <label class="field"
                >样式名称<input
                  v-model="templateName"
                  maxlength="24"
                  placeholder="例如：课间温柔提醒"
                  @keydown.enter.prevent="saveStyle" /></label
              ><button
                type="button"
                class="button secondary"
                :disabled="!templateName.trim() || !validNoticeStyle(style)"
                @click="saveStyle"
              >
                <Save :size="16" />保存样式
              </button>
            </div>
            <ul v-if="saved.length" class="notice-saved-list">
              <li v-for="preset in saved" :key="preset.id">
                <button
                  type="button"
                  class="text-button"
                  @click="apply(preset)"
                >
                  <i
                    :style="{ background: preset.style.backgroundColor }"
                  ></i
                  >{{ preset.name }}</button
                ><button
                  type="button"
                  class="icon-button"
                  :aria-label="`删除样式 ${preset.name}`"
                  @click="removeStyle(preset.id)"
                >
                  <Trash2 :size="14" />
                </button>
              </li>
            </ul>
            <p v-if="feedback" role="status" class="muted small">
              {{ feedback }}
            </p>
          </section>
        </div>
      </div>
      <p v-if="custom && !designedClient" class="notice subtle">
        自定义效果需要教室电脑升级到
        <a href="/download" target="_blank" rel="noopener"
          >0.4.13 或更新版本</a
        >。可以先设计和保存样式，覆盖安装后即可发送。
      </p>
      <p class="muted small">
        仅发给「{{
          device.name
        }}」。到时自动关闭，新提醒替换这台电脑上的旧提醒。倒计时结束后显示“时间到”5
        秒。
      </p>
      <div class="notice-send-actions">
        <button class="button primary" :disabled="sendDisabled">
          <Send :size="16" />发送到这台电脑</button
        ><button
          type="button"
          class="button secondary"
          :disabled="stopDisabled"
          @click="emit('stop')"
        >
          <Square :size="14" />停止这台电脑的提醒
        </button>
      </div>
      <p v-if="disabled" class="muted small">
        可以先设计和预览。发送需要班主任权限、高级许可及在线且未暂停的教室电脑；快捷配色支持
        0.4.12 及以上版本。
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
.notice-design-tabs {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}
.notice-design-tabs button,
.notice-presets button {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 9px 12px;
  border: 1px solid #dfe5ee;
  border-radius: 10px;
  background: white;
  cursor: pointer;
  color: #415573;
}
.notice-design-tabs button[aria-pressed="true"] {
  border-color: #8aaef2;
  color: #2460c8;
  background: #edf3ff;
}
.notice-presets {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 22px;
}
.notice-presets i,
.notice-saved-list i {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 1px solid #0001;
  flex-shrink: 0;
}
.notice-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 26px;
}
.notice-inputs {
  min-width: 0;
}
.notice-options {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
.notice-override {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 12px;
  line-height: 1.6;
  margin: 16px 0;
}
.notice-override input {
  width: 16px;
  height: 16px;
}
.notice-color input {
  padding: 5px;
  height: 42px;
  width: 100%;
  cursor: pointer;
}
.notice-color span {
  color: #718198;
  font-size: 11px;
  font-weight: 400;
}
.notice-range {
  position: relative;
  margin: 16px 0;
}
.notice-range output {
  position: absolute;
  right: 0;
  top: 0;
  font-variant-numeric: tabular-nums;
  color: #526b8d;
}
.notice-range input {
  padding: 0;
  width: 100%;
  accent-color: #3674da;
}
.notice-preview-column {
  min-width: 0;
}
.notice-saved {
  margin-top: 22px;
  padding: 18px;
  border: 1px solid #e2e8f1;
  border-radius: 14px;
}
.notice-saved h3 {
  font-size: 14px;
  margin: 0;
}
.notice-save-row {
  display: flex;
  align-items: flex-end;
  gap: 10px;
}
.notice-save-row .field {
  flex: 1;
  min-width: 0;
  margin: 0;
}
.notice-save-row .button {
  white-space: nowrap;
}
.notice-saved-list {
  list-style: none;
  margin: 16px 0 0;
  padding: 0;
}
.notice-saved-list li {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  border-top: 1px solid #eef1f5;
  padding: 6px 0;
}
.notice-saved-list .text-button {
  overflow-wrap: anywhere;
  text-align: left;
}
.notice-playing {
  font-size: 12px;
  color: #217356;
}
.notice-send-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
@media (max-width: 760px) {
  .notice-layout {
    grid-template-columns: 1fr;
  }
  .notice-preview-column {
    grid-row: 1;
  }
  .notice-modes {
    grid-template-columns: repeat(2, 1fr);
  }
  .notice-save-row {
    flex-wrap: wrap;
  }
}
</style>
