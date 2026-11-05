<script setup lang="ts">
import {
  computed,
  nextTick,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import { Play, Pause, RotateCcw } from "@lucide/vue";
import { boardFonts } from "../boardAppearance";
import {
  defaultNoticeStyle,
  noticeFill,
  type ScreenNoticeInput,
} from "../screenNotice";
const props = defineProps<{ notice: ScreenNoticeInput }>();
const host = ref<HTMLElement>();
const card = ref<HTMLElement>(),
  fitScale = ref(1);
const scale = ref(0.25),
  playing = ref(false),
  remaining = ref(30),
  replay = ref(0);
const style = computed(
  () => props.notice.style ?? defaultNoticeStyle(props.notice.tone),
);
const scrolling = computed(() =>
  ["marquee", "barrage"].includes(props.notice.mode),
);
const duration = computed(() =>
  Number.isFinite(props.notice.duration)
    ? Math.max(5, Math.min(3600, props.notice.duration))
    : 30,
);
const lines = computed(() =>
  props.notice.mode === "barrage"
    ? (props.notice.message || "在这里写下提醒")
        .split("\n")
        .filter(Boolean)
        .slice(0, 3)
    : [(props.notice.message || "在这里写下提醒").replaceAll("\n", " ")],
);
const laneHeight = computed(
  () => (style.value.fontSize * 1.4 + 48) * lines.value.length,
);
const placement = computed(() =>
  style.value.position === "center"
    ? (1080 - laneHeight.value) / 2
    : style.value.position === "bottom"
      ? 1080 - laneHeight.value - 60
      : 60,
);
const timerText = computed(() => {
  const seconds = Math.max(
    0,
    playing.value ? remaining.value : props.notice.duration || 0,
  );
  return seconds
    ? `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`
    : "时间到";
});
let observer: ResizeObserver, ticker: ReturnType<typeof setInterval>;
let started = 0;
function play() {
  remaining.value = duration.value;
  started = performance.now();
  replay.value++;
  playing.value = true;
}
function fitCard() {
  fitScale.value = card.value
    ? Math.min(1, 860 / card.value.offsetHeight)
    : 1;
}
watch(
  () => props.notice,
  () => {
    playing.value = false;
    void nextTick(fitCard);
  },
  { deep: true },
);
onMounted(() => {
  observer = new ResizeObserver(() => {
    if (host.value) scale.value = host.value.clientWidth / 1920;
  });
  if (host.value) observer.observe(host.value);
  void nextTick(fitCard);
  ticker = setInterval(() => {
    if (!playing.value) return;
    const seconds = duration.value - (performance.now() - started) / 1000;
    remaining.value = Math.max(0, Math.ceil(seconds));
    if (seconds <= (props.notice.mode === "countdown" ? -5 : 0))
      playing.value = false;
  }, 200);
});
onUnmounted(() => {
  observer?.disconnect();
  clearInterval(ticker);
});
</script>
<template>
  <div class="notice-preview-wrap">
    <div class="notice-preview-toolbar">
      <strong>效果预览</strong>
      <button
        type="button"
        class="text-button"
        @click="playing ? (playing = false) : play()"
      >
        <Pause v-if="playing" :size="15" /><Play v-else :size="15" />{{
          playing ? "暂停预览" : "播放预览"
        }}
      </button>
      <button
        type="button"
        class="icon-button"
        aria-label="重新播放预览"
        @click="play"
      >
        <RotateCcw :size="15" />
      </button>
    </div>
    <div ref="host" class="notice-preview" aria-label="醒目提醒效果预览">
      <div
        class="notice-stage"
        :style="{
          transform: `scale(${scale})`,
          color: style.textColor,
          fontFamily: boardFonts[style.fontFamily],
          fontSize: `${style.fontSize}px`,
          fontWeight: style.bold ? 700 : 400,
        }"
      >
        <div class="notice-demo-desktop" aria-hidden="true">
          <span>教室桌面</span>
          <div></div>
          <div></div>
          <div></div>
        </div>
        <template v-if="scrolling">
          <div
            :key="replay"
            class="notice-lane"
            :class="{ playing }"
            :style="{
              top: `${placement}px`,
              height: `${laneHeight}px`,
              background:
                notice.mode === 'marquee'
                  ? noticeFill(style)
                  : 'transparent',
            }"
          >
            <div
              v-for="(line, i) in lines"
              :key="i"
              class="notice-flow-line"
              :class="{ reverse: style.direction === 'right' }"
              :style="{
                top: `${i * (style.fontSize * 1.4 + 48) + 6}px`,
                borderRadius:
                  notice.mode === 'barrage' ? `${style.radius}px` : '0',
                background:
                  notice.mode === 'barrage'
                    ? noticeFill(style)
                    : 'transparent',
                animationDuration: `${style.flowSeconds}s`,
                animationDelay: `${i * 1.6}s`,
              }"
            >
              {{ line }}
            </div>
          </div>
        </template>
        <div
          v-else
          class="notice-static"
          :class="[style.surface, style.position]"
          :style="{
            background:
              style.surface === 'fullscreen'
                ? noticeFill(style)
                : 'transparent',
          }"
        >
          <div
            ref="card"
            class="notice-static-card"
            :style="{
              transform: `scale(${fitScale})`,
              transformOrigin:
                style.surface === 'card'
                  ? `${style.position === 'top' ? 'top' : style.position === 'bottom' ? 'bottom' : 'center'} center`
                  : 'center',
              width: `${style.width}%`,
              borderRadius: `${style.radius}px`,
              background:
                style.surface === 'card'
                  ? noticeFill(style)
                  : 'transparent',
            }"
          >
            <div class="notice-static-label">
              {{ notice.mode === "countdown" ? "课堂倒计时" : "课堂提醒" }}
            </div>
            <div class="notice-static-message">
              {{ notice.message || "在这里写下提醒" }}
            </div>
            <strong
              v-if="notice.mode === 'countdown'"
              class="notice-count"
              >{{ timerText }}</strong
            >
          </div>
        </div>
        <span class="notice-demo-close" aria-hidden="true">关闭提醒 ×</span>
      </div>
    </div>
    <p class="muted small">
      按常见教室宽屏预览。编辑时静止展示，点击播放查看滚动或倒计时。
    </p>
  </div>
</template>
<style scoped>
.notice-preview-wrap {
  min-width: 0;
}
.notice-preview-toolbar {
  display: flex;
  gap: 12px;
  align-items: center;
  margin-bottom: 10px;
  font-size: 13px;
}
.notice-preview-toolbar strong {
  margin-right: auto;
}
.notice-preview {
  width: 100%;
  aspect-ratio: 16/9;
  overflow: hidden;
  position: relative;
  border-radius: 16px;
  background: #dde6ee;
  border: 1px solid #d8e0ec;
}
.notice-stage {
  position: absolute;
  width: 1920px;
  height: 1080px;
  transform-origin: top left;
  overflow: hidden;
  line-height: 1.4;
}
.notice-demo-desktop {
  position: absolute;
  inset: 0;
  background: linear-gradient(135deg, #e9eff6, #ccd9e6);
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 40px;
  padding: 170px 70px 70px;
}
.notice-demo-desktop span {
  position: absolute;
  left: 70px;
  top: 65px;
  color: #677c92;
  font-size: 36px;
  font-weight: 400;
}
.notice-demo-desktop div {
  background: #ffffff70;
  border-radius: 35px;
}
.notice-demo-desktop div:first-of-type {
  grid-row: span 2;
}
.notice-lane {
  position: absolute;
  left: 0;
  width: 100%;
  overflow: hidden;
}
.notice-flow-line {
  position: absolute;
  left: 50%;
  transform: translateX(-50%);
  white-space: pre;
  padding: 18px 28px;
}
.playing .notice-flow-line {
  animation: notice-left linear infinite;
  animation-fill-mode: backwards;
}
.playing .notice-flow-line.reverse {
  animation-name: notice-right;
}
.notice-static {
  position: absolute;
  inset: 0;
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 110px 0;
}
.notice-static.card.top {
  align-items: flex-start;
}
.notice-static.card.bottom {
  align-items: flex-end;
}
.notice-static-card {
  padding: 48px 56px;
  text-align: center;
  flex-shrink: 0;
  box-sizing: border-box;
}
.notice-static-label {
  font-size: 26px;
  font-weight: 400;
  opacity: 0.75;
  margin-bottom: 28px;
}
.notice-static-message {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.notice-count {
  display: block;
  font-size: 144px;
  line-height: 1.1;
  margin-top: 28px;
  font-variant-numeric: tabular-nums;
}
.notice-demo-close {
  position: absolute;
  right: 20px;
  top: 20px;
  padding: 10px 18px;
  background: white;
  color: #334155;
  border-radius: 4px;
  font:
    16px "Microsoft YaHei",
    sans-serif;
}
@keyframes notice-left {
  from {
    left: 100%;
    transform: translateX(0);
  }
  to {
    left: 0;
    transform: translateX(-100%);
  }
}
@keyframes notice-right {
  from {
    left: 0;
    transform: translateX(-100%);
  }
  to {
    left: 100%;
    transform: translateX(0);
  }
}
@media (prefers-reduced-motion: reduce) {
  .playing .notice-flow-line {
    animation: none;
  }
}
</style>
