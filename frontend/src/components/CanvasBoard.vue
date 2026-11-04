<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import type { Classroom, Content, Layout, CanvasWidget } from "../domain";
import { dateKey, timeLabel } from "../domain";
import { widgetLabels } from "../canvas";
import { canvasPageCount, canvasPages } from "../canvasPresentation";
import { boardFonts, panelImageStyle, shapeStyle } from "../boardAppearance";
import LiquidGlassLayer from "./LiquidGlassLayer.vue";
const props = withDefaults(
  defineProps<{
    classroom: Classroom;
    contents: Content[];
    layout: Layout;
    now: number;
    page?: number;
    demo?: boolean;
    pageMap?: ReturnType<typeof canvasPages>;
    materialOnly?: boolean;
    outputSize?: { width: number; height: number };
  }>(),
  { page: 1, demo: false },
);
const emit = defineEmits<{ pages: [number] }>();
const holder = ref<HTMLElement>(),
  measuredScale = ref(0.4);
const scale = computed(() =>
  props.outputSize ? props.outputSize.width / 1600 : measuredScale.value,
);
let observer: ResizeObserver;
let resizeFrame = 0;
const measureContext = document.createElement("canvas").getContext("2d")!;
const items = computed(
  () => props.layout.canvas?.filter((i) => i.visible) ?? [],
);
const pageMap = computed(
  () =>
    props.pageMap ??
    canvasPages(
      props.layout,
      props.classroom,
      props.contents,
      measureContext,
      props.now,
    ),
);
const count = computed(() => canvasPageCount(pageMap.value));
const pageIndex = computed(() =>
  Math.max(0, Math.min(props.page, count.value) - 1),
);
watch(count, (value) => emit("pages", value), { immediate: true });
function countdown(item: CanvasWidget) {
  const source = item.dataSource === "custom" ? item : props.classroom;
  const days = Math.round(
    (Date.parse(source.eventDate ?? "") - Date.parse(dateKey(props.now))) /
      86400000,
  );
  return {
    title: source.event ? `距离${source.event}` : "事件倒计时",
    value:
      source.event && days >= 0
        ? days === 0
          ? "就是今天"
          : `${days} 天`
        : "等待设置",
    date: source.eventDate || "",
  };
}
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
  const fill =
    item.surface === "none"
      ? "transparent"
      : `color-mix(in srgb, ${item.fillColor} ${item.opacity}%,transparent)`;
  return {
    left: `${item.x / 16}%`,
    top: `${item.y}px`,
    width: `${item.w / 16}%`,
    height: `${item.h}px`,
    fontSize: `${item.fontSize}px`,
    fontFamily: boardFonts[item.fontFamily ?? "sans"],
    fontWeight: item.fontWeight ?? 400,
    color: item.textColor || "var(--board-text)",
    borderRadius: `${item.radius}px`,
    ...(item.kind === "shape"
      ? shapeStyle(
          item.shape ?? "rectangle",
          item.radius,
          item.vertices?.length ? item.vertices : undefined,
        )
      : {}),
    textAlign: item.align,
    backgroundColor: item.backgroundImage ? item.fillColor : fill,
    backgroundImage: item.backgroundImage
      ? `linear-gradient(${fill},${fill}),url("${item.backgroundImage}")`
      : "none",
    backgroundSize: `100% 100%,${item.backgroundFit ?? "cover"}`,
    backgroundPosition: `center,${item.backgroundPosition ?? "center"}`,
    backgroundRepeat: "no-repeat",
    "--widget-line": `${item.fontSize * (item.lineHeight ?? 1.5)}px`,
    "--widget-subject-size": `${item.fontSize * 0.55}px`,
    "--widget-title-size": `${item.titleSize ?? 28}px`,
    "--widget-title-line": `${(item.titleSize ?? 28) * 1.5}px`,
    "--widget-body-top": `${26 + (item.titleSize ?? 28) * 1.5}px`,
    "--widget-display-size": `${item.fontSize + 24}px`,
  };
}
function entries(item: CanvasWidget) {
  const pages = pageMap.value[item.id]!;
  return pages[pageIndex.value % pages.length] ?? [];
}
function heading(item: CanvasWidget) {
  const first = entries(item)[0];
  const title = item.title || widgetLabels[item.kind];
  return item.kind === "homework" && first?.continued && !first.showSubject
    ? `${title} · 续`
    : title;
}
onMounted(() => {
  if (props.outputSize) return;
  observer = new ResizeObserver((entries) => {
    const width = entries[0]!.contentRect.width;
    // 下一帧再更新缩放，避免在尺寸通知中触发嵌套的布局刷新。
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      measuredScale.value = width / 1600;
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
      :style="{
        ...background,
        transform: `scale(${scale}, ${outputSize ? outputSize.height / 900 : scale})`,
      }"
    >
      <div class="board-shell board-surface" :style="panelImageStyle(layout)">
        <div class="canvas-board-content">
          <section
            v-for="item in items"
            :key="item.id"
            class="canvas-widget"
            :data-widget-id="item.id"
            :class="[`widget-${item.kind}`, `widget-surface-${item.surface}`]"
            :style="style(item)"
            :aria-label="item.title || widgetLabels[item.kind]"
          >
            <LiquidGlassLayer
              v-if="
                item.surface === 'glass' &&
                item.kind !== 'shape' &&
                layout.material !== 'solid'
              "
              :width="item.w"
              :height="item.h"
              :radius="item.radius"
              :scale="scale"
            />
            <img
              v-if="item.kind === 'image' && item.image"
              :src="item.image"
              alt="老师添加的图片"
              draggable="false"
            />
            <div
              v-else-if="item.kind === 'image' && !materialOnly"
              class="canvas-image-placeholder"
            >
              选择一张图片
            </div>
            <template v-else-if="item.kind === 'datetime' && !materialOnly"
              ><div class="canvas-clock">
                <strong>{{ classroom.name }}</strong
                ><time>{{ timeLabel(now) }}</time>
                <p>{{ dateText }}</p>
              </div></template
            >
            <template v-else-if="item.kind === 'countdown' && !materialOnly"
              ><div class="canvas-countdown">
                <p>{{ countdown(item).title }}</p>
                <strong>{{ countdown(item).value }}</strong>
                <small>{{ countdown(item).date }}</small>
              </div></template
            >
            <template v-else-if="item.kind !== 'shape' && !materialOnly"
              ><h3 v-if="item.kind !== 'text' && item.showTitle !== false">
                {{ heading(item) }}
              </h3>
              <div
                class="canvas-widget-body"
                :class="{
                  'free-text': item.kind === 'text' || item.showTitle === false,
                }"
              >
                <div
                  v-for="entry in entries(item)"
                  :key="entry.id"
                  class="canvas-widget-paragraph"
                  :class="{
                    'current-course':
                      item.kind === 'timetable' &&
                      entry.id === `course-${classroom.currentPeriod}`,
                  }"
                >
                  <div v-if="entry.showSubject" class="canvas-entry-subject">
                    <span
                      >{{ entry.subject
                      }}{{ entry.continued ? " · 续" : "" }}</span
                    >
                  </div>
                  <div>{{ entry.lines.join("\n") }}</div>
                </div>
                <p v-if="!entries(item).length" class="canvas-widget-empty">
                  {{
                    item.kind === "homework"
                      ? item.subjectFilter
                        ? `暂无${item.subjectFilter}作业`
                        : "今日暂无作业"
                      : item.kind === "todo"
                        ? "暂无提醒"
                        : item.kind === "timetable"
                          ? "暂无课程"
                          : ""
                  }}
                </p>
              </div></template
            >
          </section>
        </div>
      </div>
    </div>
  </div>
</template>
