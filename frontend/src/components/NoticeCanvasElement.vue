<script setup lang="ts">
import { ref, watch, nextTick, onMounted, onUnmounted } from "vue";
import type { NoticeElement } from "../noticeCanvas";
import { rgba } from "../noticeCanvas";
import { boardFonts } from "../boardAppearance";
const props = defineProps<{ element: NoticeElement; remaining: number }>();
const text = ref<HTMLElement>();
const size = ref(props.element.fontSize);
let observer: ResizeObserver | undefined;
let fitSerial = 0;
async function fit() {
  const serial = ++fitSerial;
  size.value = props.element.fontSize;
  await nextTick();
  const el = text.value;
  if (!el || serial !== fitSerial) return;
  // 在固定设计坐标中测量；浏览器与播放端均缩小长文以容纳在组件内。
  for (
    let i = 0;
    i < 70 &&
    serial === fitSerial &&
    (el.scrollHeight > el.clientHeight + 1 ||
      el.scrollWidth > el.clientWidth + 1);
    i++
  ) {
    size.value = Math.max(0.25, size.value * 0.9);
    await nextTick();
  }
}
watch(
  () => [
    props.element.text,
    props.element.fontFamily,
    props.element.fontSize,
    props.element.bold,
    props.element.align,
    props.element.w,
    props.element.h,
    props.remaining <= 0,
  ],
  fit,
);
onMounted(() => {
  observer = new ResizeObserver(fit);
  if (text.value) observer.observe(text.value);
  void fit();
});
onUnmounted(() => observer?.disconnect());
function clock() {
  const s = Math.max(0, Math.ceil(props.remaining));
  return s
    ? `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`
    : "时间到";
}
</script>
<template>
  <div
    class="notice-element-content"
    :style="{
      borderRadius: element.radius + 'px',
      background:
        element.kind === 'card'
          ? rgba(element.fillColor, element.opacity)
          : 'transparent',
    }"
  >
    <img
      v-if="element.kind === 'image'"
      :src="element.image"
      alt=""
      draggable="false"
      :style="{ objectFit: element.imageFit, opacity: element.opacity / 100 }"
    />
    <div
      v-else
      ref="text"
      class="canvas-text"
      :style="{
        color: element.textColor,
        fontFamily: boardFonts[element.fontFamily],
        fontSize: size + 'px',
        fontWeight: element.bold ? 700 : 400,
        textAlign: element.align,
        padding: element.kind === 'card' ? '16px' : '4px',
      }"
    >
      <span>{{ element.kind === "countdown" ? clock() : element.text }}</span>
    </div>
  </div>
</template>
<style scoped>
.notice-element-content {
  width: 100%;
  height: 100%;
  overflow: hidden;
  pointer-events: none;
}
img {
  width: 100%;
  height: 100%;
  display: block;
}
.canvas-text {
  display: flex;
  align-items: center;
  width: 100%;
  height: 100%;
  box-sizing: border-box;
  line-height: 1.3;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.canvas-text span {
  width: 100%;
}
</style>
