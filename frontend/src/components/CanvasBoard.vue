<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import type { Classroom, Content, Layout, CanvasWidget } from "../domain";
import { dateKey, timeLabel } from "../domain";
import { widgetLabels } from "../canvas";
import { widgetPages } from "../canvasPresentation";
const props = withDefaults(
  defineProps<{
    classroom: Classroom;
    contents: Content[];
    layout: Layout;
    now: number;
    page?: number;
    demo?: boolean;
  }>(),
  { page: 1, demo: false },
);
const emit = defineEmits<{ pages: [number] }>();
const holder = ref<HTMLElement>(),
  scale = ref(0.4);
let observer: ResizeObserver;
let resizeFrame = 0;
const measureContext = document.createElement("canvas").getContext("2d")!;
const items = computed(
  () => props.layout.canvas?.filter((i) => i.visible) ?? [],
);
const areaWidth = computed(() =>
  props.layout.iconMargin === "none" ? 1776 : 1560,
);
const pageMap = computed(() =>
  Object.fromEntries(
    items.value.map((item) => {
      measureContext.font = `${item.fontSize}px "Microsoft YaHei", sans-serif`;
      return [
        item.id,
        widgetPages(
          item,
          props.classroom,
          props.contents,
          (item.w / 1600) * areaWidth.value,
          (s) => measureContext.measureText(s).width,
        ),
      ];
    }),
  ),
);
const count = computed(() =>
  Math.max(1, ...Object.values(pageMap.value).map((p) => p.length)),
);
const pageIndex = computed(() =>
  Math.max(0, Math.min(props.page, count.value) - 1),
);
watch(count, (value) => emit("pages", value), { immediate: true });
const days = computed(() =>
  Math.ceil(
    (new Date(`${props.classroom.eventDate}T00:00:00+08:00`).getTime() -
      new Date(`${dateKey(props.now)}T00:00:00+08:00`).getTime()) /
      86400000,
  ),
);
const dateText = computed(() =>
  new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai",
    month: "long",
    day: "numeric",
    weekday: "long",
  }).format(new Date(props.now)),
);
const background = computed(() => {
  const dark = props.layout.theme === "mica_dark",
    mode = props.layout.backgroundMode ?? "mica",
    tint = dark ? "38,41,46" : "249,250,252",
    opacity = (props.layout.surfaceOpacity ?? 38) / 100;
  return {
    backgroundColor:
      mode === "color"
        ? props.layout.backgroundColor
        : dark
          ? "#25272C"
          : "#E7E8EB",
    backgroundImage:
      mode === "image" && props.layout.backgroundImage
        ? `url("${props.layout.backgroundImage}")`
        : mode === "mica"
          ? `linear-gradient(125deg,${dark ? "#292C32,#202227" : "#ECEDEF,#DDE0E5"})`
          : "none",
    "--board-glass": `linear-gradient(135deg,rgba(${tint},${Math.min(0.98, opacity + 0.03)}),rgba(${tint},${Math.max(0.1, opacity - 0.1)}))`,
  };
});
function style(item: CanvasWidget) {
  return {
    left: `${item.x / 16}%`,
    top: `${item.y}px`,
    width: `${item.w / 16}%`,
    height: `${item.h}px`,
    fontSize: `${item.fontSize}px`,
    color: item.textColor || "var(--board-text)",
    borderRadius: `${item.radius}px`,
    textAlign: item.align,
    background:
      item.surface === "none"
        ? "transparent"
        : `color-mix(in srgb, ${item.fillColor} ${item.opacity}%,transparent)`,
    "--widget-line": `${item.fontSize * 1.5}px`,
    "--widget-display-size": `${item.fontSize + 24}px`,
  };
}
function entries(item: CanvasWidget) {
  const pages = pageMap.value[item.id]!;
  return pages[pageIndex.value % pages.length] ?? [];
}
onMounted(() => {
  observer = new ResizeObserver((entries) => {
    const width = entries[0]!.contentRect.width;
    // 下一帧再更新缩放，避免在尺寸通知中触发嵌套的布局刷新。
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      scale.value = width / 1920;
    });
  });
  if (holder.value) observer.observe(holder.value);
});
onUnmounted(() => {
  observer?.disconnect();
  cancelAnimationFrame(resizeFrame);
});
</script>
<template>
  <div
    ref="holder"
    class="board-holder canvas-board"
    :class="{
      dark: layout.theme === 'mica_dark',
      solid: layout.material === 'solid',
    }"
    aria-label="自由画布预览，非设备实时截图"
  >
    <div
      class="board-canvas"
      :class="`margin-${layout.iconMargin}`"
      :style="{ ...background, transform: `scale(${scale})` }"
    >
      <div class="board-shell board-surface">
        <div class="canvas-board-content">
          <section
            v-for="item in items"
            :key="item.id"
            class="canvas-widget"
            :class="[`widget-${item.kind}`, `widget-surface-${item.surface}`]"
            :style="style(item)"
            :aria-label="item.title || widgetLabels[item.kind]"
          >
            <img
              v-if="item.kind === 'image' && item.image"
              :src="item.image"
              alt="老师添加的图片"
              draggable="false"
            />
            <div
              v-else-if="item.kind === 'image'"
              class="canvas-image-placeholder"
            >
              选择一张图片
            </div>
            <template v-else-if="item.kind === 'datetime'"
              ><div class="canvas-clock">
                <strong>{{ classroom.name }}</strong
                ><time>{{ timeLabel(now) }}</time>
                <p>{{ dateText }}</p>
              </div></template
            >
            <template v-else-if="item.kind === 'countdown'"
              ><div class="canvas-countdown">
                <p>
                  {{
                    classroom.event ? `距离${classroom.event}` : "事件倒计时"
                  }}
                </p>
                <strong>{{
                  classroom.event && days >= 0
                    ? days === 0
                      ? "就是今天"
                      : `${days} 天`
                    : "等待设置"
                }}</strong
                ><small>{{
                  classroom.eventDate || "在班级设置中填写事件和日期"
                }}</small>
              </div></template
            >
            <template v-else-if="item.kind !== 'shape'"
              ><h3 v-if="item.kind !== 'text'">
                {{ item.title || widgetLabels[item.kind] }}
              </h3>
              <div
                class="canvas-widget-body"
                :class="{ 'free-text': item.kind === 'text' }"
              >
                <p
                  v-for="entry in entries(item)"
                  :key="entry.id"
                  class="canvas-widget-paragraph"
                  :class="{
                    'current-course':
                      item.kind === 'timetable' &&
                      entry.id === `course-${classroom.currentPeriod}`,
                  }"
                >
                  {{ entry.lines.join("\n") }}
                </p>
                <p v-if="!entries(item).length" class="canvas-widget-empty">
                  {{
                    item.kind === "homework"
                      ? "今日暂无作业"
                      : item.kind === "todo"
                        ? "暂无提醒"
                        : item.kind === "timetable"
                          ? "暂无课程"
                          : "在班级设置中填写寄语"
                  }}
                </p>
              </div></template
            >
          </section>
        </div>
        <footer class="canvas-board-status">
          <span
            >{{ demo ? "示例内容 · " : "" }}设计预览 {{ timeLabel(now) }}</span
          ><span>第 {{ pageIndex + 1 }} / {{ count }} 页</span>
        </footer>
      </div>
    </div>
  </div>
</template>
