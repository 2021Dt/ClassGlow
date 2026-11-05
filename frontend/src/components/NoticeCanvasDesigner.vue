<script setup lang="ts">
import { computed, ref, watch, onMounted, onUnmounted } from "vue";
import { session } from "../api";
import {
  type NoticeCanvas,
  type NoticeElement,
  type NoticeElementKind,
  type SavedNoticeCanvas,
  elementNames,
  newNoticeElement,
  clampNoticeElement,
  resizeNoticeElement,
  canvasProblem,
  canvasBytes,
  readNoticeCanvases,
  noticeImage,
  rgba,
} from "../noticeCanvas";
import NoticeCanvasElement from "./NoticeCanvasElement.vue";
import TaskGuide from "./TaskGuide.vue";
const props = defineProps<{ modelValue: NoticeCanvas; duration: number }>();
const emit = defineEmits<{ "update:modelValue": [value: NoticeCanvas] }>();
const draft = ref(props.modelValue),
  selectedId = ref(props.modelValue.elements[0]?.id ?? "");
const selected = computed(() =>
  draft.value.elements.find((e) => e.id === selectedId.value),
);
const history = ref<NoticeCanvas[]>([]),
  future = ref<NoticeCanvas[]>([]);
const host = ref<HTMLElement>(),
  scale = ref(0.5),
  upload = ref<HTMLInputElement>();
const feedback = ref(""),
  busy = ref(false),
  name = ref("");
const playing = ref(false),
  remaining = ref(props.duration),
  showFinished = ref(false);
const saved = ref<SavedNoticeCanvas[]>([]),
  storageKey = `classglow-notice-canvases-${session.userId}`;
try {
  saved.value = readNoticeCanvases(localStorage.getItem(storageKey));
} catch {
  /* 可在不保存时正常使用。 */
}
const problem = computed(() => canvasProblem(draft.value));
const edges = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
function handles(item: NoticeElement) {
  // 小组件保留一个角柄，给手指留出移动区域；全部尺寸仍可在属性里精调。
  return item.w * scale.value < 96 || item.h * scale.value < 64
    ? ["se"]
    : edges;
}
let observer: ResizeObserver | undefined,
  timer: ReturnType<typeof setInterval> | undefined,
  started = 0;
let drag:
  | {
      id: number;
      element: NoticeElement;
      x: number;
      y: number;
      edge: string;
      before: NoticeCanvas;
      target: HTMLElement;
      scale: number;
    }
  | undefined;
watch(
  () => props.modelValue,
  (v) => {
    if (!drag) draft.value = v;
  },
);
watch(() => props.duration, stop);
function stop() {
  playing.value = false;
  showFinished.value = false;
  remaining.value = props.duration;
  if (timer) clearInterval(timer);
  timer = undefined;
}
function play() {
  if (playing.value) return stop();
  if (problem.value) return;
  playing.value = true;
  showFinished.value = false;
  started = performance.now();
  remaining.value = props.duration;
  timer = setInterval(() => {
    remaining.value = props.duration - (performance.now() - started) / 1000;
    const extra = draft.value.elements.some(
      (e) => !e.hidden && e.kind === "countdown",
    )
      ? 5
      : 0;
    if (remaining.value <= -extra) {
      if (timer) clearInterval(timer);
      timer = undefined;
      playing.value = false;
      showFinished.value = true;
    }
  }, 200);
}
function commit(value: NoticeCanvas, before = draft.value) {
  stop();
  if (JSON.stringify(value) === JSON.stringify(before)) return;
  history.value = [...history.value.slice(-19), before];
  future.value = [];
  draft.value = value;
  emit("update:modelValue", value);
}
function undo() {
  const prior = history.value.pop();
  if (prior) {
    stop();
    future.value.push(draft.value);
    draft.value = prior;
    emit("update:modelValue", prior);
  }
}
function redo() {
  const next = future.value.pop();
  if (next) {
    stop();
    history.value.push(draft.value);
    draft.value = next;
    emit("update:modelValue", next);
  }
}
function patch(p: Partial<NoticeElement>) {
  if (!selected.value || (selected.value.locked && p.locked === undefined))
    return;
  const next = clampNoticeElement({ ...selected.value, ...p });
  commit({
    ...draft.value,
    elements: draft.value.elements.map((e) => (e.id === next.id ? next : e)),
  });
}
function value(event: Event) {
  return (event.target as HTMLInputElement).value;
}
function numeric(
  key: "x" | "y" | "w" | "h" | "fontSize" | "opacity" | "radius",
  event: Event,
) {
  const n = Number(value(event)),
    bounds = {
      x: [0, 1552],
      y: [0, 852],
      w: [48, 1600],
      h: [48, 900],
      fontSize: [24, 160],
      opacity: [0, 100],
      radius: [0, 120],
    }[key];
  if (Number.isFinite(n))
    patch({ [key]: Math.max(bounds[0]!, Math.min(bounds[1]!, Math.round(n))) });
  (event.target as HTMLInputElement).value = String(
    selected.value?.[key] ?? "",
  );
}
function background(
  key: "backgroundColor" | "backgroundOpacity",
  event: Event,
) {
  commit({
    ...draft.value,
    [key]: key === "backgroundOpacity" ? Number(value(event)) : value(event),
  });
}
function add(kind: NoticeElementKind, image = "") {
  if (draft.value.elements.length >= 12) {
    feedback.value = "最多 12 个组件，请先删除不再使用的组件。";
    return;
  }
  const item = newNoticeElement(kind, {
    image,
    x: 300 + draft.value.elements.length * 32,
    y: 240 + draft.value.elements.length * 24,
  });
  const next = {
    ...draft.value,
    elements: [...draft.value.elements, clampNoticeElement(item)],
  };
  if (canvasBytes(next) > 524288) {
    feedback.value = "画布超过 512 KB，请先删除或更换较小的图片。";
    return;
  }
  commit(next);
  selectedId.value = item.id;
  feedback.value = `已添加${elementNames[kind]}，可拖动位置或在下方修改。`;
}
async function imageFile(event: Event) {
  const input = event.target as HTMLInputElement,
    file = input.files?.[0];
  input.value = "";
  if (!file) return;
  busy.value = true;
  try {
    add("image", await noticeImage(file));
  } catch (e) {
    feedback.value =
      e instanceof Error ? e.message : "图片读取失败，请重新选择。";
  } finally {
    busy.value = false;
  }
}
function remove() {
  if (selected.value && !selected.value.locked) {
    commit({
      ...draft.value,
      elements: draft.value.elements.filter((e) => e.id !== selectedId.value),
    });
    selectedId.value = draft.value.elements.at(-1)?.id ?? "";
  }
}
function duplicate() {
  if (!selected.value || draft.value.elements.length >= 12) return;
  const item = clampNoticeElement({
    ...selected.value,
    id: crypto.randomUUID(),
    x: selected.value.x + 32,
    y: selected.value.y + 32,
    locked: false,
  });
  const next = { ...draft.value, elements: [...draft.value.elements, item] };
  if (canvasBytes(next) > 524288) {
    feedback.value = "复制后会超过 512 KB，请减少图片。";
    return;
  }
  commit(next);
  selectedId.value = item.id;
}
function layer(direction: number) {
  const items = [...draft.value.elements],
    i = items.findIndex((e) => e.id === selectedId.value),
    j = i + direction;
  if (i < 0 || j < 0 || j >= items.length || selected.value?.locked) return;
  [items[i], items[j]] = [items[j]!, items[i]!];
  commit({ ...draft.value, elements: items });
}
function start(event: PointerEvent, item: NoticeElement, edge = "") {
  if (event.button !== 0 || drag || playing.value) return;
  event.preventDefault();
  event.stopPropagation();
  selectedId.value = item.id;
  if (item.locked) return;
  const target = event.currentTarget as HTMLElement;
  target.focus();
  target.setPointerCapture(event.pointerId);
  drag = {
    id: event.pointerId,
    element: { ...item },
    x: event.clientX,
    y: event.clientY,
    edge,
    before: draft.value,
    target,
    scale: scale.value,
  };
}
function move(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.id) return;
  const dx = (event.clientX - drag.x) / drag.scale,
    dy = (event.clientY - drag.y) / drag.scale;
  const item = drag.edge
    ? resizeNoticeElement(drag.element, drag.edge, dx, dy)
    : clampNoticeElement({
        ...drag.element,
        x: drag.element.x + dx,
        y: drag.element.y + dy,
      });
  draft.value = {
    ...drag.before,
    elements: drag.before.elements.map((e) => (e.id === item.id ? item : e)),
  };
}
function finish(event: PointerEvent, cancel = false) {
  if (!drag || event.pointerId !== drag.id) return;
  const prior = drag;
  drag = undefined;
  if (prior.target.hasPointerCapture(prior.id))
    prior.target.releasePointerCapture(prior.id);
  if (cancel) draft.value = prior.before;
  else commit(draft.value, prior.before);
}
function keyboard(event: KeyboardEvent) {
  if (
    (event.target as HTMLElement).closest(
      "input,textarea,select,[contenteditable=true]",
    ) ||
    drag
  )
    return;
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
    event.preventDefault();
    event.shiftKey ? redo() : undo();
    return;
  }
  if (playing.value || !selected.value || selected.value.locked) return;
  if (event.key === "Delete") {
    event.preventDefault();
    remove();
  }
  const delta = {
    ArrowLeft: [-1, 0],
    ArrowRight: [1, 0],
    ArrowUp: [0, -1],
    ArrowDown: [0, 1],
  }[event.key];
  if (delta) {
    event.preventDefault();
    const step = event.shiftKey ? 10 : 1;
    patch({
      x: selected.value.x + delta[0]! * step,
      y: selected.value.y + delta[1]! * step,
    });
  }
}
function persist(next: SavedNoticeCanvas[]) {
  try {
    localStorage.setItem(storageKey, JSON.stringify(next));
    saved.value = next;
    return true;
  } catch {
    feedback.value = "浏览器空间不足或禁止保存，请删除旧方案后重试。";
    return false;
  }
}
function save() {
  const title = name.value.trim();
  if (!title || problem.value) return;
  const existing = saved.value.find((s) => s.name === title);
  if (!existing && saved.value.length >= 3) {
    feedback.value = "最多保存 3 个画布方案，请先删除旧方案。";
    return;
  }
  if (
    persist([
      ...saved.value.filter((s) => s !== existing),
      {
        id: existing?.id ?? crypto.randomUUID(),
        name: title,
        canvas: JSON.parse(JSON.stringify(draft.value)),
      },
    ])
  ) {
    feedback.value = existing ? "同名方案已更新。" : "方案已保存到此浏览器。";
    name.value = "";
  }
}
onMounted(() => {
  observer = new ResizeObserver(() => {
    if (host.value) scale.value = host.value.clientWidth / 1600;
  });
  if (host.value) observer.observe(host.value);
});
onUnmounted(() => {
  observer?.disconnect();
  if (timer) clearInterval(timer);
});
</script>
<template>
  <section
    class="notice-canvas-designer"
    aria-label="自由提醒画布"
    @keydown="keyboard"
  >
    <TaskGuide
      name="notice-canvas"
      title="自由画布怎么设计？"
      :steps="[
        {
          title: '添加内容',
          text: '文字用于说明，卡片自带底色，图片可展示图示；倒计时跟随播放时长。最多 12 个组件。',
        },
        {
          title: '摆放和调整',
          text: '点选组件后拖动位置，拖边框上的手柄缩放。手机可在属性中填写位置和尺寸；图层列表可选到被遮挡的组件。',
        },
        {
          title: '预览，再发送',
          text: '播放预览只在这里演示。保存方案便于下次使用；确认目标电脑和时长后，再点击发送。',
        },
      ]"
    />
    <div class="canvas-tools" role="group" aria-label="添加提醒组件">
      <button
        v-for="kind in ['text', 'card', 'countdown'] as const"
        :key="kind"
        type="button"
        :disabled="draft.elements.length >= 12"
        @click="add(kind)"
      >
        ＋ {{ elementNames[kind] }}
      </button>
      <button
        type="button"
        :disabled="busy || draft.elements.length >= 12"
        @click="upload?.click()"
      >
        {{ busy ? "处理图片…" : "＋ 图片" }}
      </button>
      <input
        ref="upload"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        hidden
        @change="imageFile"
      />
      <span class="tool-spacer"></span>
      <button type="button" :disabled="!history.length" @click="undo">
        撤销</button
      ><button type="button" :disabled="!future.length" @click="redo">
        重做
      </button>
      <button
        type="button"
        :disabled="
          !!problem ||
          !Number.isInteger(duration) ||
          duration < 5 ||
          duration > 3600
        "
        :aria-pressed="playing"
        @click="play"
      >
        {{ playing ? "结束预览" : "播放预览" }}
      </button>
    </div>
    <div class="canvas-workspace">
      <div class="canvas-main">
        <div
          ref="host"
          class="canvas-host"
          :class="{ 'is-playing': playing }"
          :style="{ height: 900 * scale + 'px' }"
        >
          <div
            class="canvas-stage"
            :style="{
              transform: `scale(${scale})`,
              background: rgba(draft.backgroundColor, draft.backgroundOpacity),
            }"
            @pointermove="move"
            @pointerup="finish($event)"
            @pointercancel="finish($event, true)"
            @lostpointercapture="finish($event, true)"
            @pointerdown.self="selectedId = ''"
          >
            <template v-if="!showFinished">
              <div
                v-for="item in draft.elements.filter((e) => !e.hidden)"
                :key="item.id"
                class="canvas-item"
                :class="{
                  selected: !playing && selectedId === item.id,
                  locked: item.locked,
                }"
                :style="{
                  left: item.x + 'px',
                  top: item.y + 'px',
                  width: item.w + 'px',
                  height: item.h + 'px',
                }"
                :tabindex="playing ? -1 : 0"
                role="button"
                :aria-label="`${elementNames[item.kind]}组件：${item.text || elementNames[item.kind]}`"
                :aria-pressed="selectedId === item.id"
                @focus="selectedId = item.id"
                @pointerdown="start($event, item)"
                @keydown.enter.prevent="selectedId = item.id"
                @keydown.space.prevent="selectedId = item.id"
              >
                <NoticeCanvasElement
                  :element="item"
                  :remaining="playing ? remaining : duration"
                />
                <template
                  v-if="!playing && selectedId === item.id && !item.locked"
                >
                  <span
                    v-for="edge in handles(item)"
                    :key="edge"
                    class="resize-grip"
                    :class="'grip-' + edge"
                    :style="{
                      width: `${24 / scale}px`,
                      height: `${24 / scale}px`,
                    }"
                    aria-hidden="true"
                    @pointerdown.stop="start($event, item, edge)"
                  ></span>
                </template>
              </div>
            </template>
          </div>
          <span v-if="showFinished" class="preview-ended"
            >预览结束，提醒已关闭
            <button type="button" @click="stop">继续编辑</button></span
          >
        </div>
        <p class="canvas-hint">
          {{
            playing
              ? "正在本页演示，不会发送到教室电脑。"
              : "点选组件后拖动；小屏幕可用下方位置、尺寸精调。"
          }}
          画布 16:9，将等比适配教室屏幕。
        </p>
        <div class="canvas-background">
          <label
            >画布底色<input
              type="color"
              :value="draft.backgroundColor"
              @change="background('backgroundColor', $event)"
          /></label>
          <label
            >底色不透明度 {{ draft.backgroundOpacity }}%<input
              type="range"
              min="0"
              max="100"
              :value="draft.backgroundOpacity"
              @change="background('backgroundOpacity', $event)"
          /></label>
        </div>
        <p class="canvas-hint">
          0% 为透明底，可透出教室桌面。图片会压缩后嵌入，整张画布最多 512
          KB（当前 {{ Math.ceil(canvasBytes(draft) / 1024) }} KB）。
        </p>
      </div>
      <aside class="canvas-properties" aria-label="组件属性">
        <h3>
          图层
          <small>{{ draft.elements.length }}/12 · 上面的图层显示在前面</small>
        </h3>
        <div class="canvas-layers">
          <button
            v-for="item in [...draft.elements].reverse()"
            :key="item.id"
            type="button"
            :aria-pressed="selectedId === item.id"
            @click="
              selectedId = item.id;
              stop();
            "
          >
            {{ elementNames[item.kind] }} ·
            {{ item.text.slice(0, 18) || "未命名" }}
            {{ item.hidden ? "（隐藏）" : "" }}
            {{ item.locked ? "（锁定）" : "" }}
          </button>
        </div>
        <template v-if="selected">
          <div class="canvas-tools">
            <button type="button" @click="patch({ locked: !selected.locked })">
              {{ selected.locked ? "解锁" : "锁定" }}</button
            ><button
              type="button"
              :disabled="selected.locked"
              @click="patch({ hidden: !selected.hidden })"
            >
              {{ selected.hidden ? "显示" : "隐藏" }}</button
            ><button
              type="button"
              :disabled="draft.elements.length >= 12"
              @click="duplicate"
            >
              复制</button
            ><button type="button" :disabled="selected.locked" @click="remove">
              删除
            </button>
            <button
              type="button"
              :disabled="selected.locked || selected === draft.elements.at(-1)"
              @click="layer(1)"
            >
              上移一层</button
            ><button
              type="button"
              :disabled="selected.locked || selected === draft.elements[0]"
              @click="layer(-1)"
            >
              下移一层
            </button>
          </div>
          <fieldset :disabled="selected.locked">
            <legend>
              {{ elementNames[selected.kind] }}属性{{
                selected.locked ? " · 已锁定" : ""
              }}
            </legend>
            <label v-if="selected.kind === 'text' || selected.kind === 'card'"
              >组件文字<textarea
                :value="selected.text"
                maxlength="240"
                rows="3"
                @input="patch({ text: value($event) })"
              />
            </label>
            <p v-if="selected.kind === 'countdown'" class="canvas-hint">
              数字跟随本条提醒的播放时长，结束后显示“时间到”5 秒。
            </p>
            <div class="canvas-fields">
              <label
                v-for="field in [
                  ['x', '水平位置'],
                  ['y', '垂直位置'],
                  ['w', '宽度'],
                  ['h', '高度'],
                ] as const"
                :key="field[0]"
                >{{ field[1]
                }}<input
                  type="number"
                  :value="selected[field[0]]"
                  :min="field[0] === 'w' || field[0] === 'h' ? 48 : 0"
                  :max="field[0] === 'x' || field[0] === 'w' ? 1600 : 900"
                  step="1"
                  @change="numeric(field[0], $event)"
              /></label>
              <template v-if="selected.kind !== 'image'">
                <label
                  >字体<select
                    :value="selected.fontFamily"
                    @change="
                      patch({
                        fontFamily: value(
                          $event,
                        ) as NoticeElement['fontFamily'],
                      })
                    "
                  >
                    <option value="sans">黑体</option>
                    <option value="serif">宋体</option>
                    <option value="kai">楷体</option>
                  </select></label
                >
                <label
                  >字号<input
                    type="number"
                    :value="selected.fontSize"
                    min="24"
                    max="160"
                    @change="numeric('fontSize', $event)"
                /></label>
                <label
                  >对齐<select
                    :value="selected.align"
                    @change="
                      patch({ align: value($event) as NoticeElement['align'] })
                    "
                  >
                    <option value="left">居左</option>
                    <option value="center">居中</option>
                    <option value="right">居右</option>
                  </select></label
                >
                <label
                  >字重<select
                    :value="selected.bold ? 'bold' : 'normal'"
                    @change="patch({ bold: value($event) === 'bold' })"
                  >
                    <option value="bold">加粗</option>
                    <option value="normal">常规</option>
                  </select></label
                >
                <label
                  >文字颜色<input
                    type="color"
                    :value="selected.textColor"
                    @change="patch({ textColor: value($event) })"
                /></label>
              </template>
              <label v-if="selected.kind === 'card'"
                >卡片底色<input
                  type="color"
                  :value="selected.fillColor"
                  @change="patch({ fillColor: value($event) })"
              /></label>
              <label v-if="selected.kind === 'image'"
                >图片适配<select
                  :value="selected.imageFit"
                  @change="
                    patch({
                      imageFit: value($event) as NoticeElement['imageFit'],
                    })
                  "
                >
                  <option value="contain">完整显示</option>
                  <option value="cover">填满裁剪</option>
                </select></label
              >
              <template
                v-if="selected.kind === 'image' || selected.kind === 'card'"
                ><label
                  >不透明度 %<input
                    type="number"
                    :value="selected.opacity"
                    min="0"
                    max="100"
                    @change="numeric('opacity', $event)" /></label
                ><label
                  >圆角<input
                    type="number"
                    :value="selected.radius"
                    min="0"
                    max="120"
                    @change="numeric('radius', $event)" /></label
              ></template>
            </div>
          </fieldset>
        </template>
        <p v-else class="canvas-hint">点选画布或图层中的组件后，在这里编辑。</p>
      </aside>
    </div>
    <p v-if="problem" role="alert" class="canvas-error">{{ problem }}</p>
    <p v-if="feedback" role="status" class="canvas-hint">{{ feedback }}</p>
    <details class="canvas-saved">
      <summary>我的画布方案（{{ saved.length }}/3）</summary>
      <p class="canvas-hint">
        包含文字和图片，保存在当前账号的此浏览器。最多 3
        个方案，同名保存会更新；可随时删除。
      </p>
      <div class="canvas-tools">
        <label
          >方案名称<input
            v-model="name"
            maxlength="24"
            placeholder="例如：课前准备"
            @keydown.enter.prevent="save" /></label
        ><button
          type="button"
          :disabled="!name.trim() || !!problem"
          @click="save"
        >
          保存画布方案
        </button>
      </div>
      <ul>
        <li v-for="item in saved" :key="item.id">
          <button
            type="button"
            @click="
              commit(JSON.parse(JSON.stringify(item.canvas)));
              selectedId = draft.elements[0]?.id ?? '';
            "
          >
            使用 {{ item.name }}</button
          ><button
            type="button"
            :aria-label="`删除画布方案 ${item.name}`"
            @click="persist(saved.filter((s) => s.id !== item.id))"
          >
            删除
          </button>
        </li>
      </ul>
    </details>
  </section>
</template>
<style scoped>
.notice-canvas-designer {
  min-width: 0;
  margin-bottom: 24px;
}
.canvas-tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 12px 0;
}
.canvas-tools button,
.canvas-saved button {
  padding: 9px 12px;
  min-height: 44px;
  border-radius: 9px;
  border: 1px solid #d8e3ed;
  background: #fff;
  color: #31506b;
  font-size: 12px;
}
.canvas-tools button[aria-pressed="true"] {
  background: #eaf2ff;
  color: #215cb7;
}
.tool-spacer {
  flex: 1;
}
.canvas-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 290px;
  gap: 18px;
  align-items: start;
}
.canvas-main {
  min-width: 0;
}
.canvas-host {
  width: 100%;
  position: relative;
  overflow: hidden;
  border-radius: 10px;
  background: linear-gradient(130deg, #dce6ec, #afc7d6);
  outline: 1px solid #b8c9d8;
}
.canvas-stage {
  position: relative;
  width: 1600px;
  height: 900px;
  transform-origin: top left;
}
.canvas-item {
  position: absolute;
  touch-action: none;
  cursor: move;
  outline: none;
}
.canvas-item.selected {
  outline: 3px solid #3e87ff;
}
.canvas-item.locked {
  cursor: default;
}
.is-playing .canvas-item {
  pointer-events: none;
}
.resize-grip {
  position: absolute;
  z-index: 2;
  display: grid;
  place-items: center;
  touch-action: none;
}
.resize-grip::after {
  content: "";
  width: 44%;
  height: 44%;
  border: 2px solid #2d74df;
  border-radius: 50%;
  background: white;
}
.grip-nw {
  left: 0;
  top: 0;
  transform: translate(-50%, -50%);
  cursor: nwse-resize;
}
.grip-n {
  left: 50%;
  top: 0;
  transform: translate(-50%, -50%);
  cursor: ns-resize;
}
.grip-ne {
  left: 100%;
  top: 0;
  transform: translate(-50%, -50%);
  cursor: nesw-resize;
}
.grip-e {
  left: 100%;
  top: 50%;
  transform: translate(-50%, -50%);
  cursor: ew-resize;
}
.grip-se {
  left: 100%;
  top: 100%;
  transform: translate(-50%, -50%);
  cursor: nwse-resize;
}
.grip-s {
  left: 50%;
  top: 100%;
  transform: translate(-50%, -50%);
  cursor: ns-resize;
}
.grip-sw {
  left: 0;
  top: 100%;
  transform: translate(-50%, -50%);
  cursor: nesw-resize;
}
.grip-w {
  left: 0;
  top: 50%;
  transform: translate(-50%, -50%);
  cursor: ew-resize;
}
.canvas-hint {
  color: #708195;
  font-size: 12px;
  line-height: 1.8;
  margin: 9px 0;
}
.canvas-background {
  display: flex;
  gap: 18px;
  flex-wrap: wrap;
  align-items: center;
}
.canvas-background label {
  flex: 1;
  min-width: 120px;
}
label {
  display: grid;
  gap: 6px;
  font-size: 12px;
  color: #4b6174;
  min-width: 0;
}
input:not([type="range"]),
select,
textarea {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  border: 1px solid #dce4ec;
  border-radius: 8px;
  padding: 9px;
  font: inherit;
  color: #293e50;
  background: #fff;
  min-height: 44px;
}
input[type="color"] {
  padding: 4px;
  width: 100%;
  max-width: 120px;
}
.canvas-properties {
  border: 1px solid #dce5ee;
  border-radius: 12px;
  padding: 14px;
  min-width: 0;
}
.canvas-properties h3 {
  margin: 0 0 10px;
  font-size: 14px;
}
.canvas-properties h3 small {
  display: block;
  margin-top: 5px;
  color: #708195;
  font-size: 11px;
  font-weight: 400;
}
.canvas-layers {
  display: grid;
  gap: 4px;
  max-height: 210px;
  overflow: auto;
}
.canvas-layers button {
  text-align: left;
  min-height: 44px;
  border: 1px solid transparent;
  border-radius: 7px;
  padding: 8px;
  background: #f6f8fb;
  font-size: 12px;
  overflow-wrap: anywhere;
}
.canvas-layers button[aria-pressed="true"] {
  background: #eaf2ff;
  border-color: #7eabec;
}
.canvas-properties fieldset {
  padding: 0;
  border: 0;
  min-width: 0;
  margin: 0;
}
.canvas-properties legend {
  font-size: 13px;
  font-weight: 600;
  margin: 8px 0 12px;
}
.canvas-fields {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-top: 12px;
}
.canvas-error {
  color: #b63e4b;
  font-size: 13px;
}
.canvas-saved {
  border-top: 1px solid #e0e8ef;
  padding: 12px 0;
}
.canvas-saved summary {
  cursor: pointer;
  min-height: 44px;
  padding-top: 10px;
  font-size: 14px;
}
.canvas-saved ul {
  list-style: none;
  padding: 0;
  display: grid;
  gap: 8px;
}
.canvas-saved li {
  display: flex;
  gap: 8px;
}
.canvas-saved li button:first-child {
  flex: 1;
  text-align: left;
}
.preview-ended {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  font-size: 13px;
}
.preview-ended button {
  padding: 10px;
  min-height: 44px;
  border-radius: 9px;
}
@media (max-width: 1000px) {
  .canvas-workspace {
    grid-template-columns: 1fr;
  }
  .canvas-properties {
    width: 100%;
    box-sizing: border-box;
  }
  .canvas-layers {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    max-height: 160px;
  }
}
@media (max-width: 600px) {
  input:not([type="range"]),
  select,
  textarea {
    font-size: 16px;
  }
  .canvas-tools {
    gap: 6px;
  }
  .canvas-tools button {
    flex-grow: 1;
  }
  .tool-spacer {
    display: none;
  }
  .canvas-background {
    gap: 12px;
  }
  .canvas-fields {
    gap: 10px;
  }
}
</style>
