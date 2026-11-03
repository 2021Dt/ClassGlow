<script setup lang="ts">
import { computed, ref, toRaw } from "vue";
import { GripVertical, Move } from "@lucide/vue";
import type { GridKey, Layout } from "../domain";

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
const rect = computed(() => props.modelValue.grid[selected.value]);
const overlaps = computed(() =>
  keys.some((a, i) =>
    keys.slice(i + 1).some((b) => {
      if (!props.modelValue.modules[a] || !props.modelValue.modules[b])
        return false;
      const x = props.modelValue.grid[a],
        y = props.modelValue.grid[b];
      return (
        x.x < y.x + y.w && y.x < x.x + x.w && x.y < y.y + y.h && y.y < x.y + x.h
      );
    }),
  ),
);
function update(field: "x" | "y" | "w" | "h", value: number) {
  if (props.disabled || !Number.isInteger(value)) return;
  const next = structuredClone(toRaw(props.modelValue));
  const r = next.grid[selected.value];
  r[field] = value;
  r.x = Math.max(0, Math.min(12 - r.w, r.x));
  r.y = Math.max(0, Math.min(12 - r.h, r.y));
  emit("update:modelValue", next);
}
function preset(balance: boolean) {
  if (props.disabled) return;
  const next = structuredClone(toRaw(props.modelValue)),
    split = balance ? 6 : 7;
  next.grid = {
    homework: { x: 0, y: 0, w: split, h: 12 },
    todo: { x: split, y: 0, w: 12 - split, h: 5 },
    timetable: { x: split, y: 5, w: 12 - split, h: 7 },
  };
  emit("update:modelValue", next);
}
let drag: {
  key: GridKey;
  startX: number;
  startY: number;
  x: number;
  y: number;
  pointer: number;
} | null = null;
function start(event: PointerEvent, key: GridKey) {
  selected.value = key;
  if (props.disabled) return;
  const r = props.modelValue.grid[key];
  drag = {
    key,
    startX: event.clientX,
    startY: event.clientY,
    x: r.x,
    y: r.y,
    pointer: event.pointerId,
  };
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}
function move(event: PointerEvent) {
  if (!drag || !surface.value) return;
  const bounds = surface.value.getBoundingClientRect(),
    r = props.modelValue.grid[drag.key];
  const next = structuredClone(toRaw(props.modelValue));
  next.grid[drag.key] = {
    ...r,
    x: Math.max(
      0,
      Math.min(
        12 - r.w,
        drag.x +
          Math.round(((event.clientX - drag.startX) * 12) / bounds.width),
      ),
    ),
    y: Math.max(
      0,
      Math.min(
        12 - r.h,
        drag.y +
          Math.round(((event.clientY - drag.startY) * 12) / bounds.height),
      ),
    ),
  };
  emit("update:modelValue", next);
}
</script>

<template>
  <section class="layout-editor">
    <div class="section-heading">
      <h2><Move :size="18" />模块网格</h2>
      <span>12 列 × 12 行</span>
    </div>
    <p class="muted small">
      拖动模块调整位置，或选择模块后用下方控件精确调整。页头和更新栏固定，保持学生阅读位置稳定。
    </p>
    <div class="record-actions">
      <button
        type="button"
        class="text-button"
        :disabled="disabled"
        @click="preset(false)"
      >
        作业优先布局</button
      ><button
        type="button"
        class="text-button"
        :disabled="disabled"
        @click="preset(true)"
      >
        均衡分栏布局
      </button>
    </div>
    <div ref="surface" class="layout-map" :class="{ invalid: overlaps }">
      <button
        v-for="key in keys.filter((k) => modelValue.modules[k])"
        :key="key"
        type="button"
        :class="{ selected: selected === key }"
        :style="{
          gridColumn: `${modelValue.grid[key].x + 1} / span ${modelValue.grid[key].w}`,
          gridRow: `${modelValue.grid[key].y + 1} / span ${modelValue.grid[key].h}`,
        }"
        :aria-label="`调整${labels[key]}`"
        @pointerdown="start($event, key)"
        @pointermove="move"
        @pointerup="drag = null"
        @pointercancel="drag = null"
        @click="selected = key"
      >
        <GripVertical :size="16" /><span>{{ labels[key] }}</span>
      </button>
    </div>
    <p v-if="overlaps" class="field-error" role="alert">
      模块出现重叠，请调整位置。重叠布局不能发布。
    </p>
    <div class="grid-settings">
      <label class="field"
        >模块<select v-model="selected">
          <option v-for="key in keys" :key="key" :value="key">
            {{ labels[key] }}
          </option>
        </select></label
      ><label class="field"
        >宽度<select
          :value="rect.w"
          :disabled="disabled"
          @change="
            update('w', Number(($event.target as HTMLSelectElement).value))
          "
        >
          <option v-for="w in [5, 6, 7, 8, 12]" :key="w" :value="w">
            {{ w }} 格
          </option>
        </select></label
      ><label class="field"
        >高度<select
          :value="rect.h"
          :disabled="disabled"
          @change="
            update('h', Number(($event.target as HTMLSelectElement).value))
          "
        >
          <option
            v-for="h in selected === 'timetable'
              ? [7, 8, 12]
              : [4, 5, 6, 7, 8, 12]"
            :key="h"
            :value="h"
          >
            {{ h }} 格
          </option>
        </select></label
      ><label class="field"
        >左侧列数<input
          type="number"
          min="0"
          :max="12 - rect.w"
          :value="rect.x"
          :disabled="disabled"
          @change="
            update('x', Number(($event.target as HTMLInputElement).value))
          " /></label
      ><label class="field"
        >顶部行数<input
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
  </section>
</template>
