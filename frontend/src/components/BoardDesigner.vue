<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import {
  BookOpen,
  Bell,
  CalendarDays,
  Clock3,
  Timer,
  MessageSquare,
  Type,
  Image as ImageIcon,
  Square,
  Undo2,
  Redo2,
  Copy,
  Trash2,
  Eye,
  EyeOff,
  LockKeyhole,
  Unlock,
  Layers,
  Plus,
  Minus,
  Move,
  Send,
  ChevronLeft,
  ChevronRight,
  Upload,
  ArrowUp,
  ArrowDown,
  Save,
  FolderOpen,
  Monitor,
  Maximize2,
  Minimize2,
} from "@lucide/vue";
import type {
  CanvasWidget,
  Classroom,
  Content,
  Layout,
  WidgetKind,
} from "../domain";
import { layoutIssue } from "../domain";
import { clone } from "../store";
import { session } from "../api";
import {
  canvasTemplate,
  clampWidget,
  newWidget,
  toCanvas,
  widgetLabels,
  widgetImage,
  widgetMinimum,
} from "../canvas";
import CanvasBoard from "./CanvasBoard.vue";
import BackgroundControls from "./BackgroundControls.vue";
const props = defineProps<{
  modelValue: Layout;
  classroom: Classroom;
  contents: Content[];
  now: number;
  disabled: boolean;
  publishError?: string;
  publishNotice?: string;
}>();
const emit = defineEmits<{ "update:modelValue": [Layout]; publish: [] }>();
const selected = ref(""),
  propertyTab = ref("component"),
  zoom = ref(100),
  snap = ref(true),
  page = ref(1),
  pages = ref(1),
  feedback = ref(""),
  imageBusy = ref(false),
  focused = ref(false),
  viewport = ref<HTMLElement>(),
  fitWidth = ref(700),
  surface = ref<HTMLElement>();
const history = ref<Layout[]>([]),
  future = ref<Layout[]>([]),
  template = ref("standard");
const model = computed(() =>
  props.modelValue.editor === "canvas"
    ? props.modelValue
    : toCanvas(props.modelValue),
);
const items = computed(() => model.value.canvas ?? []),
  current = computed(() => items.value.find((i) => i.id === selected.value));
const issue = computed(() => layoutIssue(model.value));
watch(
  () => props.publishNotice,
  (value) => {
    if (value) feedback.value = "";
  },
);
let viewportObserver: ResizeObserver;
let resizeFrame = 0;
const palette = [
  { kind: "homework", icon: BookOpen },
  { kind: "todo", icon: Bell },
  { kind: "timetable", icon: CalendarDays },
  { kind: "datetime", icon: Clock3 },
  { kind: "countdown", icon: Timer },
  { kind: "message", icon: MessageSquare },
  { kind: "text", icon: Type },
  { kind: "image", icon: ImageIcon },
  { kind: "shape", icon: Square },
] as const;
const storageKey = computed(
  () => `kanban-layout-input-${session.userId}-${props.classroom.id}`,
);
function checkpoint() {
  history.value.push(clone(model.value));
  if (history.value.length > 20) history.value.shift();
  future.value = [];
}
function change(value: Layout, record = true) {
  if (props.disabled) return;
  if (record) checkpoint();
  emit("update:modelValue", value);
  feedback.value = "";
}
function patchItem(
  patch: Partial<CanvasWidget>,
  record = true,
  id = selected.value,
) {
  change(
    {
      ...model.value,
      canvas: items.value.map((item) =>
        item.id === id ? clampWidget({ ...item, ...patch }) : item,
      ),
    },
    record,
  );
}
function add(kind: WidgetKind) {
  if (props.disabled || items.value.length >= 24) return;
  const offset = (items.value.length % 8) * 24,
    item = newWidget(kind, { x: 48 + offset, y: 48 + offset });
  let suffix = 2;
  while (items.value.some((entry) => entry.title === item.title))
    item.title = `${widgetLabels[kind]} ${suffix++}`;
  change({ ...model.value, editor: "canvas", canvas: [...items.value, item] });
  selected.value = item.id;
  propertyTab.value = "component";
}
function select(id: string) {
  selected.value = id;
  propertyTab.value = "component";
}
function undo() {
  const previous = history.value.pop();
  if (!previous || props.disabled) return;
  future.value.push(clone(model.value));
  emit("update:modelValue", previous);
}
function redo() {
  const next = future.value.pop();
  if (!next || props.disabled) return;
  history.value.push(clone(model.value));
  emit("update:modelValue", next);
}
function duplicate() {
  if (!current.value || items.value.length >= 24) return;
  const item = clampWidget({
    ...clone(current.value),
    id: crypto.randomUUID(),
    x: current.value.x + 24,
    y: current.value.y + 24,
    locked: false,
  });
  change({ ...model.value, canvas: [...items.value, item] });
  selected.value = item.id;
}
function remove() {
  if (!current.value || current.value.locked) return;
  change({
    ...model.value,
    canvas: items.value.filter((i) => i.id !== selected.value),
  });
  selected.value = "";
}
function layer(direction: number) {
  const list = [...items.value],
    index = list.findIndex((i) => i.id === selected.value),
    target = index + direction;
  if (index < 0 || target < 0 || target >= list.length) return;
  [list[index], list[target]] = [list[target]!, list[index]!];
  change({ ...model.value, canvas: list });
}
function align(axis: "x" | "y", position: number) {
  if (!current.value) return;
  const size = axis === "x" ? 1600 - current.value.w : 900 - current.value.h;
  patchItem({ [axis]: Math.round(size * position) });
}
function applyTemplate() {
  change({
    ...model.value,
    editor: "canvas",
    canvas: canvasTemplate(template.value as "standard"),
    reverse: false,
  });
  selected.value = "";
  page.value = 1;
}
function saveDraft() {
  try {
    localStorage.setItem(storageKey.value, JSON.stringify(model.value));
    feedback.value = "设计草稿已保存在当前浏览器，尚未发布。";
  } catch {
    feedback.value = "本机存储空间不足，请减少图片后重试。";
  }
}
function restoreDraft() {
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey.value) ?? "null");
    if (!saved || saved.editor !== "canvas") {
      feedback.value = "当前班级没有本机设计草稿。";
      return;
    }
    const invalid = layoutIssue(saved);
    if (invalid) throw new Error(invalid);
    change({ ...saved, revision: props.modelValue.revision });
    selected.value = "";
    feedback.value = "已读取本机草稿，请预览后再发布。";
  } catch (e) {
    feedback.value = `读取失败：${(e as Error).message}`;
  }
}
async function upload(event: Event) {
  const input = event.target as HTMLInputElement,
    file = input.files?.[0],
    id = selected.value;
  if (!file) return;
  imageBusy.value = true;
  try {
    const image = await widgetImage(file);
    patchItem({ image }, true, id);
  } catch (e) {
    feedback.value = (e as Error).message;
  } finally {
    imageBusy.value = false;
    input.value = "";
  }
}
function number(
  field: "x" | "y" | "w" | "h" | "fontSize" | "radius" | "opacity",
  event: Event,
) {
  const input = event.target as HTMLInputElement;
  const value = Number(input.value);
  if (!input.value || !Number.isFinite(value)) {
    if (event.type === "blur" && current.value)
      input.value = String(current.value[field]);
    return;
  }
  // 输入中允许先键入小于最小尺寸的前几位，完整数值才更新画布；离开输入框时再归整。
  if (
    event.type === "input" &&
    (value < Number(input.min) || value > Number(input.max))
  )
    return;
  patchItem({ [field]: value }, false);
  if (event.type === "blur" && current.value)
    input.value = String(current.value[field]);
}
let drag: {
  id: string;
  mode: "move" | "resize";
  x: number;
  y: number;
  item: CanvasWidget;
  width: number;
  height: number;
  pointer: number;
} | null = null;
function start(
  event: PointerEvent,
  item: CanvasWidget,
  mode: "move" | "resize",
) {
  select(item.id);
  if (props.disabled || item.locked || event.button !== 0 || !surface.value)
    return;
  event.preventDefault();
  checkpoint();
  const bounds = surface.value.getBoundingClientRect();
  drag = {
    id: item.id,
    mode,
    x: event.clientX,
    y: event.clientY,
    item: clone(item),
    width: bounds.width,
    height: bounds.height,
    pointer: event.pointerId,
  };
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}
function move(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.pointer) return;
  const step = snap.value && !event.altKey ? 8 : 1,
    dx = ((event.clientX - drag.x) * 1600) / drag.width,
    dy = ((event.clientY - drag.y) * 900) / drag.height,
    round = (v: number) => Math.round(v / step) * step;
  patchItem(
    drag.mode === "move"
      ? { x: round(drag.item.x + dx), y: round(drag.item.y + dy) }
      : {
          // 拉伸固定左上角，碰到画布边界就停止，不把整个组件反向推走。
          w: Math.min(1600 - drag.item.x, round(drag.item.w + dx)),
          h: Math.min(900 - drag.item.y, round(drag.item.h + dy)),
        },
    false,
    drag.id,
  );
}
function keyboard(event: KeyboardEvent) {
  if (event.key === "Escape") focused.value = false;
  if (
    props.disabled ||
    /INPUT|TEXTAREA|SELECT/.test((event.target as HTMLElement).tagName)
  )
    return;
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
    event.preventDefault();
    event.shiftKey ? redo() : undo();
    return;
  }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "d") {
    event.preventDefault();
    duplicate();
    return;
  }
  if (!current.value || current.value.locked) return;
  const steps: Record<string, [number, number]> = {
    ArrowLeft: [-1, 0],
    ArrowRight: [1, 0],
    ArrowUp: [0, -1],
    ArrowDown: [0, 1],
  };
  if (steps[event.key]) {
    event.preventDefault();
    const [x, y] = steps[event.key]!,
      step = event.shiftKey ? 10 : 1;
    patchItem({ x: current.value.x + x * step, y: current.value.y + y * step });
  } else if (event.key === "Delete" || event.key === "Backspace") {
    event.preventDefault();
    remove();
  }
}
function itemStyle(item: CanvasWidget) {
  return {
    left: `${item.x / 16}%`,
    top: `${item.y / 9}%`,
    width: `${item.w / 16}%`,
    height: `${item.h / 9}%`,
  };
}
const regionStyle = computed(() => ({
  left: `${(model.value.iconMargin === "left" ? 288 : 72) / 19.2}%`,
  width: `${(model.value.iconMargin === "none" ? 1776 : 1560) / 19.2}%`,
}));
onMounted(() => {
  viewportObserver = new ResizeObserver((entries) => {
    const bounds = entries[0]!.contentRect;
    // 画布会影响子预览的尺寸，延后到下一帧统一调整，避免通知循环。
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      fitWidth.value = Math.max(
        120,
        Math.min(bounds.width, (bounds.height * 16) / 9),
      );
    });
  });
  if (viewport.value) viewportObserver.observe(viewport.value);
  if (props.modelValue.editor !== "canvas" && !props.disabled)
    emit("update:modelValue", toCanvas(props.modelValue));
});
onUnmounted(() => {
  viewportObserver?.disconnect();
  cancelAnimationFrame(resizeFrame);
});
</script>
<template>
  <section
    class="canvas-studio"
    :class="{ 'studio-focused': focused }"
    @keydown="keyboard"
  >
    <header class="studio-toolbar">
      <div class="studio-heading">
        <span><Layers :size="18" /></span>
        <div>
          <strong>看板设计室</strong><small>像搭积木一样，设计你的教室</small>
        </div>
      </div>
      <div class="studio-history">
        <button
          class="icon-button"
          :disabled="disabled || !history.length"
          @click="undo"
          aria-label="撤销设计修改"
          title="撤销 Ctrl+Z"
        >
          <Undo2 :size="17" /></button
        ><button
          class="icon-button"
          :disabled="disabled || !future.length"
          @click="redo"
          aria-label="重做设计修改"
        >
          <Redo2 :size="17" />
        </button>
      </div>
      <div class="studio-save-actions">
        <button class="button secondary" @click="focused = !focused">
          <component :is="focused ? Minimize2 : Maximize2" :size="15" />
          <span>{{ focused ? "收起画布" : "展开画布" }}</span>
        </button>
        <button
          class="button secondary"
          :disabled="disabled"
          @click="saveDraft"
        >
          <Save :size="15" /><span>保存草稿</span></button
        ><button
          class="button primary"
          :disabled="disabled || !!issue || imageBusy"
          @click="emit('publish')"
        >
          <Send :size="15" />发布到教室
        </button>
      </div>
    </header>
    <p v-if="publishError" class="notice danger studio-notice" role="alert">
      {{ publishError }}
    </p>
    <p v-if="publishNotice" class="notice subtle studio-notice" role="status">
      {{ publishNotice }}
    </p>
    <p v-if="disabled" class="notice subtle studio-notice">
      自由设计需要有效的高级许可。基础看板仍可正常使用。
    </p>
    <div v-if="feedback" class="notice subtle studio-notice" role="status">
      {{ feedback }}
    </div>
    <p v-if="issue" class="notice warning studio-notice" role="alert">
      {{ issue }}
    </p>
    <div class="studio-body">
      <aside class="studio-library">
        <div class="studio-section-title">
          <strong>添加组件</strong><span>{{ items.length }}/24</span>
        </div>
        <p class="studio-help">点一下添加，拖动到喜欢的位置</p>
        <div class="studio-palette">
          <button
            v-for="entry in palette"
            :key="entry.kind"
            :disabled="disabled || items.length >= 24"
            @click="add(entry.kind)"
          >
            <component :is="entry.icon" :size="19" /><span>{{
              widgetLabels[entry.kind]
            }}</span
            ><Plus :size="12" />
          </button>
        </div>
        <div class="studio-template">
          <strong>从模板开始</strong
          ><select
            v-model="template"
            aria-label="选择画布模板"
            :disabled="disabled"
          >
            <option value="standard">课堂标准</option>
            <option value="columns">三栏平分</option>
            <option value="notes">灵感便签</option>
            <option value="blank">空白画布</option></select
          ><button
            class="button secondary full"
            :disabled="disabled"
            @click="applyTemplate"
          >
            应用模板</button
          ><small>会替换当前排版，可撤销恢复。</small>
        </div>
        <div class="studio-layers">
          <div class="studio-section-title">
            <strong>图层</strong><Layers :size="14" />
          </div>
          <div v-if="!items.length" class="studio-help">
            从组件库添加第一块内容
          </div>
          <article
            v-for="item in [...items].reverse()"
            :key="item.id"
            :class="{ active: selected === item.id }"
          >
            <button class="layer-name" @click="select(item.id)">
              {{ item.title || widgetLabels[item.kind] }}</button
            ><button
              class="layer-icon"
              :disabled="disabled"
              :aria-label="`${item.visible ? '隐藏' : '显示'}${item.title}`"
              @click="patchItem({ visible: !item.visible }, true, item.id)"
            >
              <component :is="item.visible ? Eye : EyeOff" :size="13" /></button
            ><button
              class="layer-icon"
              :disabled="disabled"
              :aria-label="`${item.locked ? '解锁' : '锁定'}${item.title}`"
              @click="patchItem({ locked: !item.locked }, true, item.id)"
            >
              <component :is="item.locked ? LockKeyhole : Unlock" :size="13" />
            </button>
          </article>
        </div>
      </aside>
      <div class="studio-canvas-column">
        <div class="studio-canvas-toolbar">
          <span><Move :size="14" />自由画布</span
          ><label><input type="checkbox" v-model="snap" />吸附对齐</label>
          <div>
            <button
              class="icon-button"
              :disabled="zoom <= 75"
              aria-label="缩小画布"
              @click="zoom -= 25"
            >
              <Minus :size="14" /></button
            ><button class="text-button" @click="zoom = 100">
              {{ zoom === 100 ? "适应画布" : `${zoom}%` }}</button
            ><button
              class="icon-button"
              :disabled="zoom >= 200"
              aria-label="放大画布"
              @click="zoom += 25"
            >
              <Plus :size="14" />
            </button>
          </div>
        </div>
        <div ref="viewport" class="studio-viewport">
          <div
            class="studio-stage"
            :style="{ width: `${(fitWidth * zoom) / 100}px` }"
            tabindex="0"
            aria-label="看板自由设计画布，方向键移动选中组件"
          >
            <CanvasBoard
              :classroom="classroom"
              :contents="contents"
              :layout="model"
              :now="now"
              :page="page"
              @pages="
                pages = $event;
                page = Math.min(page, $event);
              "
            />
            <div
              ref="surface"
              class="studio-interaction"
              :class="{ snapping: snap }"
              :style="regionStyle"
              @pointerdown.self="selected = ''"
            >
              <div
                v-for="item in items.filter((i) => i.visible)"
                :key="item.id"
                class="studio-object"
                :class="{ selected: selected === item.id, locked: item.locked }"
                :style="itemStyle(item)"
              >
                <button
                  class="studio-object-drag"
                  :aria-label="`选择并移动${item.title || widgetLabels[item.kind]}`"
                  @pointerdown="start($event, item, 'move')"
                  @pointermove="move"
                  @pointerup="drag = null"
                  @pointercancel="drag = null"
                  @lostpointercapture="drag = null"
                  @click="select(item.id)"
                ></button
                ><span v-if="selected === item.id" class="studio-object-label"
                  >{{ item.title || widgetLabels[item.kind] }} · {{ item.w }}×{{
                    item.h
                  }}</span
                ><button
                  v-if="selected === item.id && !item.locked && !disabled"
                  class="studio-object-resize"
                  :aria-label="`缩放${item.title || widgetLabels[item.kind]}`"
                  @pointerdown.stop="start($event, item, 'resize')"
                  @pointermove="move"
                  @pointerup="drag = null"
                  @pointercancel="drag = null"
                  @lostpointercapture="drag = null"
                ></button>
              </div>
            </div>
          </div>
        </div>
        <div class="studio-page-controls">
          <button
            class="icon-button"
            aria-label="自由画布上一页"
            :disabled="page <= 1"
            @click="page--"
          >
            <ChevronLeft :size="16" /></button
          ><span>内容预览 {{ page }} / {{ pages }}</span
          ><button
            class="icon-button"
            aria-label="自由画布下一页"
            :disabled="page >= pages"
            @click="page++"
          >
            <ChevronRight :size="16" /></button
          ><small>长内容自动分页，卡片大小保持不变</small>
        </div>
        <p class="studio-canvas-hint">
          拖动移动 · 右下角缩放 · 方向键微调 · Ctrl+D 复制 · Delete 删除
        </p>
        <div v-if="!items.length" class="studio-empty-hint">
          <Plus :size="20" /><strong>从一张空白画布开始</strong>
          <p>添加组件、写几句话，或者放一张图片。</p>
        </div>
        <div class="studio-publish-note">
          <Monitor :size="15" /><span
            >这是设计预览。点击发布后同步到教室，Windows 客户端需升级至
            0.4.0。</span
          >
        </div>
      </div>
      <aside class="studio-inspector">
        <div class="studio-inspector-tabs">
          <button
            :class="{ active: propertyTab === 'component' }"
            @click="propertyTab = 'component'"
          >
            组件属性</button
          ><button
            :class="{ active: propertyTab === 'background' }"
            @click="propertyTab = 'background'"
          >
            整体外观
          </button>
        </div>
        <div v-if="propertyTab === 'component'" class="studio-property-body">
          <div v-if="!current" class="studio-no-selection">
            <Layers :size="27" />
            <h3>选中一块组件</h3>
            <p>调整位置、字号和颜色，让每块内容都合适。</p>
            <button
              class="text-button"
              :disabled="disabled"
              @click="restoreDraft"
            >
              <FolderOpen :size="14" />读取本机设计草稿
            </button>
          </div>
          <template v-else
            ><div class="studio-selected-heading">
              <div>
                <strong>{{ widgetLabels[current.kind] }}</strong
                ><small>{{
                  current.locked
                    ? "已锁定，解锁后可拖动"
                    : "自由排列 · 支持叠放"
                }}</small>
              </div>
              <button
                class="icon-button"
                :disabled="disabled || items.length >= 24"
                aria-label="复制选中组件"
                @click="duplicate"
              >
                <Copy :size="16" /></button
              ><button
                class="icon-button destructive"
                :disabled="disabled || current.locked"
                aria-label="删除选中组件"
                @click="remove"
              >
                <Trash2 :size="16" />
              </button>
            </div>
            <fieldset :disabled="disabled || current.locked">
              <label class="field"
                >组件名称<input
                  :value="current.title"
                  maxlength="30"
                  @focus="checkpoint"
                  @input="
                    patchItem(
                      {
                        title: ($event.target as HTMLInputElement).value,
                      },
                      false,
                    )
                  " /></label
              ><label v-if="current.kind === 'text'" class="field"
                >文字内容<textarea
                  :value="current.text"
                  rows="4"
                  maxlength="2000"
                  @focus="checkpoint"
                  @input="
                    patchItem(
                      { text: ($event.target as HTMLTextAreaElement).value },
                      false,
                    )
                  "
                /></label
              ><template v-if="current.kind === 'image'"
                ><label class="studio-upload"
                  ><Upload :size="19" /><span>{{
                    imageBusy ? "正在处理图片…" : "选择组件图片"
                  }}</span
                  ><input
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    :disabled="imageBusy"
                    @change="upload"
                /></label>
                <p class="studio-help">
                  图片会压缩保存，保留原始宽高比完整显示。
                </p></template
              >
              <div class="studio-number-grid">
                <label class="field"
                  >左边距<input
                    type="number"
                    :value="current.x"
                    min="0"
                    :max="1600 - current.w"
                    @focus="checkpoint"
                    @input="number('x', $event)"
                    @blur="number('x', $event)" /></label
                ><label class="field"
                  >上边距<input
                    type="number"
                    :value="current.y"
                    min="0"
                    :max="900 - current.h"
                    @focus="checkpoint"
                    @input="number('y', $event)"
                    @blur="number('y', $event)" /></label
                ><label class="field"
                  >宽度<input
                    type="number"
                    :value="current.w"
                    :min="widgetMinimum(current.kind).w"
                    max="1600"
                    @focus="checkpoint"
                    @input="number('w', $event)"
                    @blur="number('w', $event)" /></label
                ><label class="field"
                  >高度<input
                    type="number"
                    :value="current.h"
                    :min="widgetMinimum(current.kind).h"
                    max="900"
                    @focus="checkpoint"
                    @input="number('h', $event)"
                    @blur="number('h', $event)"
                /></label>
              </div>
              <div class="studio-align">
                <button type="button" @click="align('x', 0)">靠左</button
                ><button type="button" @click="align('x', 0.5)">水平居中</button
                ><button type="button" @click="align('x', 1)">靠右</button
                ><button type="button" @click="align('y', 0)">靠上</button
                ><button type="button" @click="align('y', 0.5)">垂直居中</button
                ><button type="button" @click="align('y', 1)">靠下</button>
              </div>
              <template v-if="!['image', 'shape'].includes(current.kind)"
                ><label class="field"
                  >文字大小<input
                    type="number"
                    min="16"
                    max="56"
                    :value="current.fontSize"
                    @focus="checkpoint"
                    @input="number('fontSize', $event)"
                    @blur="number('fontSize', $event)"
                /></label>
                <div class="studio-color-row">
                  <label
                    >文字颜色<input
                      type="color"
                      :value="
                        current.textColor ||
                        (model.theme === 'mica_dark' ? '#F1F3F7' : '#252A32')
                      "
                      @change="
                        patchItem({
                          textColor: ($event.target as HTMLInputElement).value,
                        })
                      " /></label
                  ><button
                    class="text-button"
                    @click="patchItem({ textColor: '' })"
                  >
                    跟随主题
                  </button>
                </div>
                <label class="field"
                  >文字对齐<select
                    :value="current.align"
                    @change="
                      patchItem({
                        align: ($event.target as HTMLSelectElement)
                          .value as CanvasWidget['align'],
                      })
                    "
                  >
                    <option value="left">左对齐</option>
                    <option value="center">居中</option>
                  </select></label
                ></template
              ><label class="field"
                >组件底色<select
                  :value="current.surface"
                  @change="
                    patchItem({
                      surface: ($event.target as HTMLSelectElement)
                        .value as CanvasWidget['surface'],
                    })
                  "
                >
                  <option value="glass">通透卡片</option>
                  <option value="solid">自定义色块</option>
                  <option value="none">无底色</option>
                </select></label
              >
              <div v-if="current.surface !== 'none'" class="studio-color-row">
                <label
                  >底色<input
                    type="color"
                    :value="current.fillColor"
                    @change="
                      patchItem({
                        fillColor: ($event.target as HTMLInputElement).value,
                      })
                    " /></label
                ><label
                  >不透明度<input
                    type="number"
                    min="0"
                    max="100"
                    :value="current.opacity"
                    @focus="checkpoint"
                    @input="number('opacity', $event)"
                    @blur="number('opacity', $event)"
                /></label>
              </div>
              <label class="field"
                >圆角<input
                  type="range"
                  min="0"
                  max="80"
                  :value="current.radius"
                  @focus="checkpoint"
                  @input="number('radius', $event)"
                  @blur="number('radius', $event)"
              /></label>
              <div class="studio-layer-actions">
                <button class="button secondary" @click="layer(1)">
                  <ArrowUp :size="14" />上一层</button
                ><button class="button secondary" @click="layer(-1)">
                  <ArrowDown :size="14" />下一层
                </button>
              </div>
            </fieldset>
            <button
              v-if="current.locked"
              class="button secondary full"
              :disabled="disabled"
              @click="patchItem({ locked: false })"
            >
              <Unlock :size="15" />解锁组件
            </button></template
          >
        </div>
        <div v-else class="studio-property-body">
          <fieldset :disabled="disabled">
            <label class="field"
              >主题<select
                :value="model.theme"
                @change="
                  change({
                    ...model,
                    theme: ($event.target as HTMLSelectElement)
                      .value as Layout['theme'],
                  })
                "
              >
                <option value="mica_light">云母浅灰</option>
                <option value="mica_dark">云母深灰</option>
              </select></label
            ><label class="field"
              >大卡片材质<select
                :value="model.material"
                @change="
                  change({
                    ...model,
                    material: ($event.target as HTMLSelectElement)
                      .value as Layout['material'],
                  })
                "
              >
                <option value="subtle">通透</option>
                <option value="solid">纯色</option>
              </select></label
            ><BackgroundControls
              :model-value="model"
              :disabled="disabled"
              @update:model-value="change($event)"
            /><label class="field"
              >桌面图标留白<select
                :value="model.iconMargin"
                @change="
                  change({
                    ...model,
                    iconMargin: ($event.target as HTMLSelectElement)
                      .value as Layout['iconMargin'],
                  })
                "
              >
                <option value="left">左侧</option>
                <option value="right">右侧</option>
                <option value="none">不留白</option>
              </select></label
            ><label class="field"
              >自动翻页间隔<select
                :value="model.interval"
                @change="
                  change({
                    ...model,
                    interval: Number(
                      ($event.target as HTMLSelectElement).value,
                    ) as Layout['interval'],
                  })
                "
              >
                <option :value="15">15 秒</option>
                <option :value="20">20 秒</option>
                <option :value="30">30 秒</option>
              </select></label
            >
          </fieldset>
          <button
            class="button secondary full"
            :disabled="disabled"
            @click="restoreDraft"
          >
            <FolderOpen :size="15" />读取本机设计草稿
          </button>
          <p class="studio-help">
            草稿只留在当前浏览器，发布后才会同步到教室。
          </p>
        </div>
      </aside>
    </div>
  </section>
</template>
