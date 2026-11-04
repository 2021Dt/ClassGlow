<script setup lang="ts">
import { computed, ref } from "vue";
import { GripVertical, Move, MoveDiagonal2 } from "@lucide/vue";
import {
  layoutIssue,
  type GridKey,
  type GridRect,
  type Layout,
} from "../domain";

const props = defineProps<{ modelValue: Layout; disabled: boolean }>();
const emit = defineEmits<{ "update:modelValue": [Layout] }>();
const surface = ref<HTMLElement>(),
  selected = ref<GridKey>("homework");
const labels: Record<GridKey, string> = {
  homework: "今日作业",
  todo: "班级提醒",
  timetable: "今日课表",
};
const keys = Object.keys(labels) as GridKey[];
const issue = computed(() => layoutIssue(props.modelValue));
const minHeight = (key: GridKey) => (key === "timetable" ? 7 : 4);
// 所有控件都按屏幕上的实际位置编辑，兼容已有的左右镜像配置。
function visualRect(key: GridKey): GridRect {
  const r = props.modelValue.grid[key];
  return { ...r, x: props.modelValue.reverse ? 12 - r.x - r.w : r.x };
}
const rect = computed(() => visualRect(selected.value));
function place(key: GridKey, value: GridRect) {
  const next = { ...props.modelValue, grid: { ...props.modelValue.grid } };
  next.grid[key] = {
    ...value,
    x: next.reverse ? 12 - value.x - value.w : value.x,
  };
  emit("update:modelValue", next);
}
function update(field: keyof GridRect, value: number) {
  if (props.disabled || !Number.isInteger(value)) return;
  const r = { ...rect.value, [field]: value };
  r.w = Math.max(5, Math.min(12, r.w));
  r.h = Math.max(minHeight(selected.value), Math.min(12, r.h));
  r.x = Math.max(0, Math.min(12 - r.w, r.x));
  r.y = Math.max(0, Math.min(12 - r.h, r.y));
  place(selected.value, r);
}
function toggle(key: GridKey) {
  const next = {
    ...props.modelValue,
    modules: { ...props.modelValue.modules },
  };
  next.modules[key] = !next.modules[key];
  selected.value = key;
  emit("update:modelValue", next);
}
function preset(name: "homework" | "balanced" | "schedule") {
  if (props.disabled) return;
  const next = { ...props.modelValue },
    split = name === "balanced" ? 6 : 7;
  next.reverse = false;
  next.grid =
    name === "schedule"
      ? {
          timetable: { x: 0, y: 0, w: 5, h: 12 },
          todo: { x: 5, y: 0, w: 7, h: 4 },
          homework: { x: 5, y: 4, w: 7, h: 8 },
        }
      : {
          homework: { x: 0, y: 0, w: split, h: 12 },
          todo: { x: split, y: 0, w: 12 - split, h: 5 },
          timetable: { x: split, y: 5, w: 12 - split, h: 7 },
        };
  emit("update:modelValue", next);
}
let drag: {
  key: GridKey;
  mode: "move" | "resize";
  startX: number;
  startY: number;
  rect: GridRect;
  pointer: number;
  width: number;
  height: number;
} | null = null;
function start(event: PointerEvent, key: GridKey, mode: "move" | "resize") {
  selected.value = key;
  if (props.disabled || event.button !== 0 || !surface.value) return;
  event.preventDefault();
  const bounds = surface.value.getBoundingClientRect();
  drag = {
    key,
    mode,
    startX: event.clientX,
    startY: event.clientY,
    rect: visualRect(key),
    pointer: event.pointerId,
    width: bounds.width,
    height: bounds.height,
  };
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}
function move(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.pointer) return;
  const r = { ...drag.rect };
  const dx = Math.round(((event.clientX - drag.startX) * 12) / drag.width),
    dy = Math.round(((event.clientY - drag.startY) * 12) / drag.height);
  if (drag.mode === "move") {
    r.x = Math.max(0, Math.min(12 - r.w, r.x + dx));
    r.y = Math.max(0, Math.min(12 - r.h, r.y + dy));
  } else {
    // 最小尺寸保障大屏可读性；长正文继续分页，不随拖动缩小字号或撑高卡片。
    r.w = Math.max(5, Math.min(12 - r.x, r.w + dx));
    r.h = Math.max(minHeight(drag.key), Math.min(12 - r.y, r.h + dy));
  }
  place(drag.key, r);
}
function keyboard(event: KeyboardEvent, key: GridKey, resize = false) {
  const direction = {
    ArrowLeft: [-1, 0],
    ArrowRight: [1, 0],
    ArrowUp: [0, -1],
    ArrowDown: [0, 1],
  }[event.key];
  if (!direction || props.disabled) return;
  event.preventDefault();
  selected.value = key;
  const field = direction[0] ? (resize ? "w" : "x") : resize ? "h" : "y";
  update(field, rect.value[field] + (direction[0] || direction[1])!);
}
function slot(key: GridKey) {
  const r = visualRect(key);
  return {
    gridColumn: `${r.x + 1} / span ${r.w}`,
    gridRow: `${r.y + 1} / span ${r.h}`,
  };
}
</script>

<template>
  <section class="layout-editor">
    <div class="section-heading">
      <h2><Move :size="18" />大卡片内部布局</h2>
      <span>自动吸附 · 固定边界</span>
    </div>
    <p class="muted small">
      拖动小卡片调整位置，拖动右下角调整大小。手机也可使用下方控件；班级标题与底部寄语固定。
    </p>
    <div class="layout-presets" aria-label="布局预设">
      <button
        type="button"
        class="button secondary"
        :disabled="disabled"
        @click="preset('homework')"
      >
        作业优先
      </button>
      <button
        type="button"
        class="button secondary"
        :disabled="disabled"
        @click="preset('balanced')"
      >
        均衡分栏
      </button>
      <button
        type="button"
        class="button secondary"
        :disabled="disabled"
        @click="preset('schedule')"
      >
        课表在左
      </button>
    </div>
    <div class="layout-module-toggles" aria-label="显示小卡片">
      <label v-for="key in keys" :key="key"
        ><input
          type="checkbox"
          :checked="modelValue.modules[key]"
          :disabled="disabled"
          @change="toggle(key)"
        />{{ labels[key] }}</label
      >
    </div>
    <div class="layout-container-map">
      <div class="layout-fixed-band">
        <strong>班级 · 日期</strong><span>倒计时 · 时钟</span>
      </div>
      <div
        ref="surface"
        class="layout-map"
        :class="{ invalid: issue }"
        aria-label="大卡片内部布局画布"
      >
        <div
          v-for="key in keys.filter((k) => modelValue.modules[k])"
          :key="key"
          class="layout-tile"
          :class="{ selected: selected === key }"
          :style="slot(key)"
        >
          <button
            type="button"
            class="layout-tile-move"
            :disabled="disabled"
            :aria-label="`移动${labels[key]}`"
            :aria-pressed="selected === key"
            @pointerdown="start($event, key, 'move')"
            @pointermove="move"
            @pointerup="drag = null"
            @pointercancel="drag = null"
            @lostpointercapture="drag = null"
            @keydown="keyboard($event, key)"
            @click="selected = key"
          >
            <GripVertical :size="16" /><span
              ><strong>{{ labels[key] }}</strong
              ><small
                >{{ modelValue.grid[key].w }} ×
                {{ modelValue.grid[key].h }}</small
              ></span
            >
          </button>
          <button
            type="button"
            class="layout-resize"
            :disabled="disabled"
            :aria-label="`调整${labels[key]}大小`"
            @pointerdown.stop="start($event, key, 'resize')"
            @pointermove="move"
            @pointerup="drag = null"
            @pointercancel="drag = null"
            @lostpointercapture="drag = null"
            @keydown="keyboard($event, key, true)"
          >
            <MoveDiagonal2 :size="17" />
          </button>
        </div>
        <p
          v-if="!keys.some((k) => modelValue.modules[k])"
          class="layout-map-empty"
        >
          勾选上方卡片后开始布局
        </p>
      </div>
      <div class="layout-fixed-band">
        <span>班级寄语 · 更新状态</span><span>页码</span>
      </div>
    </div>
    <p v-if="issue" class="field-error" role="alert">{{ issue }}</p>
    <div class="grid-settings">
      <label class="field"
        >选中卡片<AppSelect v-model="selected">
          <option v-for="key in keys" :key="key" :value="key">
            {{ labels[key] }}{{ modelValue.modules[key] ? "" : "（隐藏）" }}
          </option>
        </AppSelect></label
      >
      <label class="field"
        >宽度<AppSelect
          :value="rect.w"
          :disabled="disabled"
          @change="
            update('w', Number(($event.target as HTMLSelectElement).value))
          "
        >
          <option v-for="w in 8" :key="w" :value="w + 4">{{ w + 4 }} 格</option>
        </AppSelect></label
      >
      <label class="field"
        >高度<AppSelect
          :value="rect.h"
          :disabled="disabled"
          @change="
            update('h', Number(($event.target as HTMLSelectElement).value))
          "
        >
          <option
            v-for="h in 13 - minHeight(selected)"
            :key="h"
            :value="h + minHeight(selected) - 1"
          >
            {{ h + minHeight(selected) - 1 }} 格
          </option>
        </AppSelect></label
      >
      <label class="field"
        >左侧留空<input
          type="number"
          min="0"
          :max="12 - rect.w"
          :value="rect.x"
          :disabled="disabled"
          @change="
            update('x', Number(($event.target as HTMLInputElement).value))
          "
      /></label>
      <label class="field"
        >顶部留空<input
          type="number"
          min="0"
          :max="12 - rect.h"
          :value="rect.y"
          :disabled="disabled"
          @change="
            update('y', Number(($event.target as HTMLInputElement).value))
          "
      /></label>
    </div>
    <p class="muted small">
      小卡片按 12 × 12 网格对齐。重叠时请调整或选择预设，再发布到教室。
    </p>
  </section>
</template>
