<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import type { Classroom, Content, GridKey, Layout } from "../domain";
import { dateKey, timeLabel } from "../domain";
import { paginate } from "../pagination";
import CanvasBoard from "./CanvasBoard.vue";
import { panelImageStyle } from "../boardAppearance";

const props = withDefaults(
  defineProps<{
    classroom: Classroom;
    contents: Content[];
    layout: Layout;
    page?: number;
    now: number;
    demo?: boolean;
  }>(),
  { page: 1, demo: false },
);
const emit = defineEmits<{ pages: [number] }>();
const holder = ref<HTMLElement>(),
  scale = ref(0.45);
let observer: ResizeObserver;
let resizeFrame = 0;
const fontSize = computed(
  () => ({ small: 32, medium: 36, large: 40 })[props.layout.font],
);
const boardWidth = computed(() =>
  props.layout.iconMargin === "none" ? 1776 : 1560,
);
const canvas = document.createElement("canvas").getContext("2d")!;
const backgroundStyle = computed(() => {
  const dark = props.layout.theme === "mica_dark",
    opacity = (props.layout.surfaceOpacity ?? 38) / 100;
  const tint = dark ? "38, 41, 46" : "249, 250, 252";
  const mode = props.layout.backgroundMode ?? "mica";
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
          ? `linear-gradient(125deg, ${dark ? "#292C32, #202227" : "#ECEDEF, #DDE0E5"})`
          : "none",
    "--board-glass": `linear-gradient(135deg, rgba(${tint}, ${Math.min(0.98, opacity + 0.03)}), rgba(${tint}, ${Math.max(0.1, opacity - 0.1)}))`,
  };
});
function dimensions(key: GridKey) {
  const r = props.layout.grid[key];
  return {
    width: (r.w * (boardWidth.value + 28)) / 12 - 28,
    height: (r.h * 688) / 12 - 28,
  };
}
function slotStyle(key: GridKey) {
  const r = props.layout.grid[key];
  return {
    gridColumn: `${(props.layout.reverse ? 12 - r.x - r.w : r.x) + 1} / span ${r.w}`,
    gridRow: `${r.y + 1} / span ${r.h}`,
  };
}
const pages = computed(() => {
  canvas.font = `${fontSize.value}px "Microsoft YaHei", sans-serif`;
  const measure = (s: string) => canvas.measureText(s).width;
  const jobs = props.layout.modules.homework
    ? props.contents.filter((c) => c.kind === "homework")
    : [];
  const todos = props.layout.modules.todo
    ? props.contents
        .filter((c) => c.kind === "todo")
        .sort((a, b) => Number(!!b.priority) - Number(!!a.priority))
    : [];
  const left = dimensions("homework"),
    right = dimensions("todo");
  const homework = paginate(
    jobs,
    Math.max(1, (left.height - 126) / (fontSize.value * 1.55)),
    left.width - 206,
    measure,
  );
  const todo = paginate(
    todos,
    Math.max(1, (right.height - 112) / (fontSize.value * 1.55)),
    right.width - 110,
    measure,
  );
  const count = Math.max(homework.length, todo.length);
  return { homework, todo, count };
});
const activePage = computed(() => Math.min(props.page, pages.value.count) - 1);
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
    year: "numeric",
    month: "long",
    day: "numeric",
    weekday: "long",
  }).format(new Date(props.now)),
);
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
import { watch } from "vue";
watch(
  () => (props.layout.editor === "canvas" ? null : pages.value.count),
  (value) => {
    if (value !== null) emit("pages", value);
  },
  { immediate: true },
);
</script>

<template>
  <CanvasBoard
    v-if="layout.editor === 'canvas'"
    :classroom="classroom"
    :contents="contents"
    :layout="layout"
    :now="now"
    :page="page"
    :demo="demo"
    @pages="emit('pages', $event)"
  />
  <div
    v-else
    ref="holder"
    class="board-holder"
    :class="{
      dark: layout.theme === 'mica_dark',
      solid: layout.material === 'solid',
    }"
    aria-label="大屏设计预览，非教室实时截图"
  >
    <div
      class="board-canvas"
      :style="{
        ...backgroundStyle,
        transform: `scale(${scale})`,
        '--board-font': `${fontSize}px`,
      }"
      :class="[`margin-${layout.iconMargin}`, { reverse: layout.reverse }]"
    >
      <div
        class="board-shell board-surface"
        :style="panelImageStyle(layout)"
        aria-label="班级展示大卡片"
      >
        <div class="board-layout-content">
          <header class="board-header">
            <div>
              <h2>{{ classroom.name }}</h2>
              <p v-if="layout.modules.datetime">{{ dateText }}</p>
            </div>
            <div
              class="board-event"
              v-if="layout.modules.countdown && classroom.event && days >= 0"
            >
              <p>距离{{ classroom.event }}</p>
              <strong>{{ days === 0 ? "就是今天" : `还有 ${days} 天` }}</strong>
            </div>
            <time v-if="layout.modules.datetime">{{ timeLabel(now) }}</time>
          </header>
          <div class="board-grid custom-grid">
            <section
              v-if="layout.modules.homework"
              class="board-homework board-surface"
              :style="slotStyle('homework')"
            >
              <h3>
                今日作业
                <span
                  >{{
                    contents.filter((c) => c.kind === "homework").length
                  }}
                  条</span
                >
              </h3>
              <div class="board-body">
                <div
                  class="board-job"
                  v-for="item in pages.homework[activePage] ?? []"
                  :key="`${item.id}-${item.continued}`"
                >
                  <strong
                    >{{ item.subject
                    }}<small v-if="item.continued">续</small></strong
                  >
                  <p>
                    <template v-for="(line, index) in item.lines" :key="index"
                      >{{ line }}<br v-if="index < item.lines.length - 1"
                    /></template>
                  </p>
                </div>
                <p
                  class="board-empty"
                  v-if="!contents.some((c) => c.kind === 'homework')"
                >
                  今天还没有发布作业
                </p>
              </div>
            </section>
            <section
              v-if="layout.modules.todo"
              class="board-reminders board-surface"
              :style="slotStyle('todo')"
            >
              <h3>今日提醒</h3>
              <div class="board-body">
                <p
                  v-for="item in pages.todo[activePage] ?? []"
                  :key="`${item.id}-${item.continued}`"
                  class="board-todo"
                >
                  <span>•</span><span>{{ item.lines.join("\n") }}</span>
                </p>
                <p
                  class="board-empty"
                  v-if="!contents.some((c) => c.kind === 'todo')"
                >
                  暂无需要提醒的事项
                </p>
              </div>
            </section>
            <section
              v-if="layout.modules.timetable"
              class="board-timetable board-surface"
              :style="slotStyle('timetable')"
            >
              <h3>
                今日课表
                <span v-if="classroom.nextPeriod" class="next-period">{{
                  classroom.nextPeriod
                }}</span>
              </h3>
              <div class="board-body">
                <div
                  v-if="classroom.timetable.length"
                  class="board-periods"
                  :class="{
                    'many-periods': classroom.timetable.length > 8,
                    'long-subjects': classroom.timetable.some(
                      (s) => s.length > 4,
                    ),
                  }"
                >
                  <p
                    v-for="(subject, index) in classroom.timetable"
                    :key="index"
                    :class="{
                      'current-course': classroom.currentPeriod === index,
                    }"
                  >
                    <span>{{ index + 1 }}</span
                    >{{ subject }}
                  </p>
                </div>
                <p v-else class="board-empty">
                  {{
                    classroom.timetableState === "configured"
                      ? "今日无课程"
                      : "课表待配置"
                  }}
                </p>
              </div>
            </section>
          </div>
          <footer class="board-footer">
            <div>
              <p v-if="layout.modules.message && classroom.message">
                {{ classroom.message }}
              </p>
              <small
                >{{ demo ? "示例内容 · " : "" }}设计预览
                {{ timeLabel(now) }}</small
              >
            </div>
            <span>第 {{ activePage + 1 }} / {{ pages.count }} 页</span>
          </footer>
        </div>
      </div>
    </div>
  </div>
</template>
