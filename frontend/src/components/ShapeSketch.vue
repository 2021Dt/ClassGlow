<script setup lang="ts">
import { computed, ref } from "vue";
import type { ShapePoint } from "../domain";

const props = defineProps<{ vertices: ShapePoint[] }>();
const emit = defineEmits<{ draw: [ShapePoint[]] }>();
const draft = ref<ShapePoint[]>();
const points = computed(() => draft.value ?? props.vertices);
const outline = computed(() =>
  points.value.map(({ x, y }) => x + "," + y).join(" "),
);
const canApply = computed(
  () => new Set(points.value.map(({ x, y }) => x + "," + y)).size >= 3,
);
function addPoint(event: PointerEvent) {
  if (!draft.value || draft.value.length >= 24) return;
  const box = (event.currentTarget as SVGElement).getBoundingClientRect();
  const x = Math.round(((event.clientX - box.left) / box.width) * 100);
  const y = Math.round(((event.clientY - box.top) / box.height) * 100);
  draft.value.push({
    x: Math.max(0, Math.min(100, x)),
    y: Math.max(0, Math.min(100, y)),
  });
}
function apply() {
  if (!draft.value || !canApply.value) return;
  emit("draw", draft.value);
  draft.value = undefined;
}
</script>

<template>
  <div class="studio-shape-sketch">
    <svg
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      role="img"
      aria-label="自由轮廓绘图区"
      :class="{ drawing: draft }"
      @pointerdown="addPoint"
    >
      <polygon
        v-if="points.length >= 3"
        :points="outline"
        fill="#b6d6ff"
        fill-rule="evenodd"
      />
      <polyline
        v-if="draft"
        :points="outline"
        fill="none"
        stroke="#0071e3"
        stroke-width="1"
      />
      <circle
        v-for="(point, index) in points"
        :key="index"
        :cx="point.x"
        :cy="point.y"
        r="1.8"
        fill="#0071e3"
      />
    </svg>
    <button
      v-if="!draft"
      type="button"
      class="button secondary"
      @click="draft = []"
    >
      重画轮廓
    </button>
    <template v-else>
      <p class="studio-help">按顺序点出轮廓 · {{ draft.length }} / 24 个顶点</p>
      <div class="studio-composition-actions">
        <button
          type="button"
          class="text-button"
          :disabled="!draft.length"
          @click="draft.pop()"
        >
          撤销顶点
        </button>
        <button type="button" class="text-button" @click="draft = undefined">
          取消
        </button>
        <button
          type="button"
          class="button primary"
          :disabled="!canApply"
          @click="apply"
        >
          应用轮廓
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.studio-shape-sketch svg {
  display: block;
  width: 100%;
  height: 124px;
  margin-bottom: 8px;
  background: #f5f7fa;
  border-radius: 8px;
}
.studio-shape-sketch svg.drawing {
  cursor: crosshair;
  touch-action: none;
}
.studio-shape-sketch .button {
  min-height: 34px;
  padding: 6px 10px;
  font-size: 12px;
}
</style>
