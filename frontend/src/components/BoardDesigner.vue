<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
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
  Maximize2,
  Minimize2,
  ArrowLeft,
  Boxes,
  Group,
  Ungroup,
  SlidersHorizontal,
  Palette,
  Megaphone,
} from "@lucide/vue";
import type {
  CanvasWidget,
  Classroom,
  Content,
  Layout,
  ImageFit,
  ImagePosition,
  ShapeKind,
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
  homeworkPriorityLayout,
  moveCanvasItems,
  resizeCanvasItem,
  type ResizeEdge,
  widgetLabels,
  widgetImage,
  widgetMinimum,
} from "../canvas";
import CanvasBoard from "./CanvasBoard.vue";
import StudioPalette from "./StudioPalette.vue";
import BackgroundControls from "./BackgroundControls.vue";
import DatePicker from "./DatePicker.vue";
import CardImageControls from "./CardImageControls.vue";
import ShapeSketch from "./ShapeSketch.vue";
import {
  boardFonts,
  defaultOutline,
  shapeLabels,
  shapeStyle,
} from "../boardAppearance";
import {
  canvasPageCount,
  canvasPages,
  textTokens,
} from "../canvasPresentation";
const props = defineProps<{
  modelValue: Layout;
  classroom: Classroom;
  contents: Content[];
  now: number;
  disabled: boolean;
  disabledReason?: string;
  publishError?: string;
  publishNotice?: string;
  standalone?: boolean;
}>();
const emit = defineEmits<{
  "update:modelValue": [Layout];
  publish: [summary: string];
}>();
const selection = ref<string[]>([]),
  multiSelect = ref(false),
  textEditor = ref<HTMLTextAreaElement>(),
  pieceName = ref(""),
  mobilePanel = ref("library"),
  propertyTab = ref("component"),
  zoom = ref(100),
  snap = ref(true),
  page = ref(1),
  playing = ref(false),
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
  selectedItems = computed(() =>
    items.value.filter((item) => selection.value.includes(item.id)),
  ),
  current = computed(() =>
    selectedItems.value.length === 1 ? selectedItems.value[0] : undefined,
  );
const editableSelection = computed(
  () =>
    !props.disabled &&
    selectedItems.value.length > 0 &&
    selectedItems.value.every((item) => !item.locked),
);
const groupedSelection = computed(() =>
  selectedItems.value.some((item) => item.groupId),
);
const subjectChoices = computed(() =>
  Array.from(
    new Set([
      ...props.classroom.subjects,
      ...props.contents
        .filter((entry) => entry.kind === "homework")
        .map((entry) => entry.subject),
      current.value?.subjectFilter ?? "",
    ]),
  ).filter(Boolean),
);
const issue = computed(() => layoutIssue(model.value));
const measureContext = document.createElement("canvas").getContext("2d")!;
const pageMap = computed(() =>
  canvasPages(
    model.value,
    props.classroom,
    props.contents,
    measureContext,
    props.now,
  ),
);
const pages = computed(() => canvasPageCount(pageMap.value));
const homeworkCards = computed(() =>
  items.value.filter((item) => item.kind === "homework" && item.visible),
);
const homeworkPages = computed(() =>
  Math.max(
    0,
    ...homeworkCards.value.map((item) => pageMap.value[item.id]!.length),
  ),
);
const rotationSummary = computed(() => {
  const total = pages.value * model.value.interval;
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  const duration =
    `${minutes ? `${minutes} 分 ` : ""}${seconds ? `${seconds} 秒` : ""}`.trim();
  const jobs =
    homeworkPages.value > 0 &&
    props.contents.some((item) => item.kind === "homework")
      ? `作业 ${homeworkPages.value} 页 · `
      : "";
  return pages.value > 1
    ? `${jobs}看板 ${pages.value} 页 · 每页 ${model.value.interval} 秒 · 一轮 ${duration}`
    : `${jobs}单页展示`;
});
const prioritySuggestion = computed(() => {
  const homework = homeworkCards.value[0];
  if (!homework || homeworkPages.value <= 1 || props.disabled) return null;
  const layout = homeworkPriorityLayout(model.value);
  if (!layout) return null;
  const proposed = canvasPages(
    layout,
    props.classroom,
    props.contents,
    measureContext,
    props.now,
  );
  const count = proposed[homework.id]!.length;
  return count < homeworkPages.value && canvasPageCount(proposed) <= pages.value
    ? { layout, count }
    : null;
});
watch(pages, (count) => {
  page.value = Math.min(page.value, count);
  if (count === 1) playing.value = false;
});
watch(
  [playing, pages, () => model.value.interval],
  ([active, count, interval], _, onCleanup) => {
    if (!active || count <= 1) return;
    const timer = setInterval(
      () => (page.value = (page.value % count) + 1),
      interval * 1000,
    );
    onCleanup(() => clearInterval(timer));
  },
);
function previewPage(next: number) {
  playing.value = false;
  page.value = next;
}
function tryHomeworkPriority() {
  const suggestion = prioritySuggestion.value;
  if (!suggestion) return;
  change(suggestion.layout);
  previewPage(1);
}
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
type Piece = { id: string; name: string; items: CanvasWidget[] };
const pieces = ref<Piece[]>([]);
const piecesKey = computed(() => "classglow-canvas-pieces-" + session.userId);
watch(
  piecesKey,
  (key) => {
    try {
      pieces.value = JSON.parse(localStorage.getItem(key) ?? "[]");
    } catch {
      pieces.value = [];
    }
  },
  { immediate: true },
);
function storePieces(next: Piece[]) {
  try {
    localStorage.setItem(piecesKey.value, JSON.stringify(next));
    pieces.value = next;
    feedback.value = "";
    return true;
  } catch {
    feedback.value = "本机积木存储空间不足，请减少图片或删除旧积木。";
    return false;
  }
}
function savePiece() {
  const name = pieceName.value.trim();
  if (!name || !selectedItems.value.length || props.disabled) return;
  const piece = {
    id: crypto.randomUUID(),
    name,
    items: clone(selectedItems.value),
  };
  if (storePieces([...pieces.value, piece])) {
    pieceName.value = "";
    feedback.value = "已保存到「我的积木」。";
  }
}
function checkpoint() {
  history.value.push(clone(model.value));
  if (history.value.length > 20) history.value.shift();
  future.value = [];
}
function change(value: Layout, record = true) {
  if (props.disabled) return;
  playing.value = false;
  if (record) checkpoint();
  emit("update:modelValue", value);
  feedback.value = "";
}
function patchItem(
  patch: Partial<CanvasWidget>,
  record = true,
  id = current.value?.id,
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
  select(item.id, false, [item]);
}
function bindSubject(subject: string) {
  if (!current.value) return;
  const oldTitle = current.value.subjectFilter
    ? `${current.value.subjectFilter}作业`
    : widgetLabels.homework;
  patchItem({
    subjectFilter: subject,
    ...(current.value.title === oldTitle
      ? { title: subject ? `${subject}作业` : widgetLabels.homework }
      : {}),
  });
}
const surfacePresets: {
  key: string;
  label: string;
  patch: Partial<CanvasWidget>;
}[] = [
  {
    key: "glass",
    label: "柔光玻璃",
    patch: {
      surface: "glass",
      fillColor: "#FFFFFF",
      opacity: 22,
      radius: 28,
      textColor: "",
    },
  },
  {
    key: "paper",
    label: "素白纸张",
    patch: {
      surface: "solid",
      fillColor: "#FFFFFF",
      opacity: 95,
      radius: 24,
      textColor: "#252A32",
    },
  },
  {
    key: "ink",
    label: "静谧深色",
    patch: {
      surface: "solid",
      fillColor: "#242936",
      opacity: 96,
      radius: 24,
      textColor: "#F4F6FA",
    },
  },
];
function activeSurface(preset: (typeof surfacePresets)[number]) {
  return (
    !!current.value &&
    Object.entries(preset.patch).every(
      ([key, value]) => current.value![key as keyof CanvasWidget] === value,
    )
  );
}
function setDataSource(value: string) {
  const dataSource = value as "classroom" | "custom";
  // 第一次改为独立内容时带入班级当前值，已有自定义内容在切换后仍保留。
  patchItem({
    dataSource,
    ...(dataSource === "custom"
      ? {
          text: current.value?.text || props.classroom.message,
          event: current.value?.event || props.classroom.event,
          eventDate: current.value?.eventDate || props.classroom.eventDate,
        }
      : {}),
  });
}
function select(id: string, extend = false, source = items.value) {
  const item = source.find((entry) => entry.id === id);
  if (!item) return;
  const ids = item.groupId
    ? source
        .filter((entry) => entry.groupId === item.groupId)
        .map((entry) => entry.id)
    : [id];
  if (!extend) selection.value = ids;
  else if (ids.every((entry) => selection.value.includes(entry)))
    selection.value = selection.value.filter((entry) => !ids.includes(entry));
  else selection.value = [...new Set([...selection.value, ...ids])];
  propertyTab.value = "component";
  if (!multiSelect.value) mobilePanel.value = "inspector";
}
function selectLayer(id: string, event: MouseEvent) {
  select(id, multiSelect.value || event.shiftKey);
}
function setShape(shape: ShapeKind) {
  if (shape === "custom" && !current.value?.vertices?.length) {
    patchItem({ shape, vertices: defaultOutline });
  } else patchItem({ shape });
}
function clickCanvas(id: string, event: MouseEvent) {
  if (event.detail === 0 || multiSelect.value || event.shiftKey)
    selectLayer(id, event);
}
function groupSelection() {
  if (!editableSelection.value || selectedItems.value.length < 2) return;
  const groupId = crypto.randomUUID();
  change({
    ...model.value,
    canvas: items.value.map((item) =>
      selection.value.includes(item.id) ? { ...item, groupId } : item,
    ),
  });
  multiSelect.value = false;
  mobilePanel.value = "inspector";
}
function ungroupSelection() {
  const groupIds = new Set(
    selectedItems.value.map((item) => item.groupId).filter(Boolean),
  );
  if (
    !editableSelection.value ||
    items.value.some(
      (item) => item.groupId && groupIds.has(item.groupId) && item.locked,
    )
  )
    return;
  change({
    ...model.value,
    canvas: items.value.map((item) =>
      item.groupId && groupIds.has(item.groupId)
        ? { ...item, groupId: null }
        : item,
    ),
  });
}
async function insertToken(event: Event) {
  const control = event.target as HTMLSelectElement;
  const token = control.value;
  control.value = "";
  const item = current.value;
  const editor = textEditor.value;
  if (!token || !item || !editor) return;
  const start = editor.selectionStart;
  const mark = "{{" + token + "}}";
  const value =
    item.text.slice(0, start) + mark + item.text.slice(editor.selectionEnd);
  if (value.length > 2000) return;
  patchItem({ text: value, dynamicText: true });
  await nextTick();
  editor.focus();
  editor.setSelectionRange(start + mark.length, start + mark.length);
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
  insertItems(selectedItems.value);
}
function insertItems(source: CanvasWidget[]) {
  if (
    props.disabled ||
    !source.length ||
    items.value.length + source.length > 24
  )
    return;
  const groupId = source.length > 1 ? crypto.randomUUID() : null;
  const inserted = moveCanvasItems(source, 24, 24).map((item) => ({
    ...item,
    id: crypto.randomUUID(),
    groupId,
    locked: false,
  }));
  const layout = { ...model.value, canvas: [...items.value, ...inserted] };
  // 临时空图片仍可继续编辑，已存积木则应能直接发布。
  if (
    new TextEncoder().encode(JSON.stringify(layout)).length >
    2 * 1024 * 1024 - 1024
  ) {
    feedback.value = "图片总量过大，请减少图片后再插入。";
    return;
  }
  change(layout);
  selection.value = inserted.map((item) => item.id);
  propertyTab.value = "component";
  mobilePanel.value = "inspector";
}
function remove() {
  if (!editableSelection.value) return;
  change({
    ...model.value,
    canvas: items.value.filter((item) => !selection.value.includes(item.id)),
  });
  selection.value = [];
}
function layer(direction: number) {
  const list = [...items.value],
    index = list.findIndex((i) => i.id === current.value?.id),
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
    canvas: canvasTemplate(
      template.value as Parameters<typeof canvasTemplate>[0],
    ),
    reverse: false,
  });
  selection.value = [];
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
    selection.value = [];
    feedback.value = "已读取本机草稿，请预览后再发布。";
  } catch (e) {
    feedback.value = `读取失败：${(e as Error).message}`;
  }
}
async function upload(event: Event) {
  const input = event.target as HTMLInputElement,
    file = input.files?.[0],
    id = current.value?.id;
  if (!file) return;
  imageBusy.value = true;
  try {
    const image = await widgetImage(file);
    if (id) patchItem({ image }, true, id);
  } catch (e) {
    feedback.value = (e as Error).message;
  } finally {
    imageBusy.value = false;
    input.value = "";
  }
}
function number(
  field:
    | "x"
    | "y"
    | "w"
    | "h"
    | "fontSize"
    | "titleSize"
    | "lineHeight"
    | "radius"
    | "opacity",
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
function updateCardImage(value: {
  targetId: string;
  image: string;
  fit: ImageFit;
  position: ImagePosition;
}) {
  patchItem(
    {
      backgroundImage: value.image,
      backgroundFit: value.fit,
      backgroundPosition: value.position,
    },
    true,
    value.targetId,
  );
}
let drag: {
  id: string;
  mode: "move" | ResizeEdge;
  x: number;
  y: number;
  item: CanvasWidget;
  members: CanvasWidget[];
  width: number;
  height: number;
  pointer: number;
  next: CanvasWidget[];
  elements: {
    id: string;
    element: HTMLElement;
    style: string;
    preview: boolean;
  }[];
} | null = null;
let gestureFrame = 0;
const resizeEdges: { edge: ResizeEdge; label: string }[] = [
  { edge: "n", label: "上边" },
  { edge: "s", label: "下边" },
  { edge: "e", label: "右边" },
  { edge: "w", label: "左边" },
  { edge: "nw", label: "左上角" },
  { edge: "ne", label: "右上角" },
  { edge: "sw", label: "左下角" },
  { edge: "se", label: "右下角" },
];
function start(
  event: PointerEvent,
  item: CanvasWidget,
  mode: "move" | ResizeEdge,
) {
  if (
    event.button !== 0 ||
    drag ||
    (mode === "move" && (multiSelect.value || event.shiftKey))
  )
    return;
  if (!selection.value.includes(item.id)) select(item.id);
  if (!editableSelection.value || !surface.value) return;
  event.preventDefault();
  playing.value = false;
  const bounds = surface.value.getBoundingClientRect();
  const ids = new Set(selectedItems.value.map((i) => i.id));
  const stage = surface.value.parentElement!;
  stage.classList.add("studio-gesturing");
  drag = {
    id: item.id,
    mode,
    x: event.clientX,
    y: event.clientY,
    item,
    members: selectedItems.value,
    width: bounds.width,
    height: bounds.height,
    pointer: event.pointerId,
    next: selectedItems.value,
    elements: Array.from(
      stage.querySelectorAll<HTMLElement>("[data-widget-id]"),
    )
      .filter((el) => ids.has(el.dataset.widgetId!))
      .map((element) => ({
        id: element.dataset.widgetId!,
        element,
        style: element.style.cssText,
        preview: element.classList.contains("canvas-widget"),
      })),
  };
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}
function move(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.pointer) return;
  const step = snap.value && !event.altKey ? 8 : 1,
    dx = ((event.clientX - drag.x) * 1600) / drag.width,
    dy = ((event.clientY - drag.y) * 900) / drag.height,
    round = (v: number) => Math.round(v / step) * step;
  if (drag.mode === "move") {
    drag.next = moveCanvasItems(drag.members, round(dx), round(dy));
  } else
    drag.next = [resizeCanvasItem(drag.item, drag.mode, round(dx), round(dy))];
  // 一帧只绘制一次临时几何；分页、玻璃重建和草稿存储都等松手后再执行。
  if (!gestureFrame) gestureFrame = requestAnimationFrame(paintGesture);
}
function paintGesture() {
  gestureFrame = 0;
  if (!drag) return;
  const next = new Map(drag.next.map((item) => [item.id, item]));
  for (const { id, element, preview } of drag.elements) {
    const item = next.get(id);
    if (!item) continue;
    element.dataset.dragging = "true";
    element.style.setProperty("--drag-left", `${item.x / 16}%`);
    element.style.setProperty("--drag-width", `${item.w / 16}%`);
    element.style.setProperty(
      "--drag-top",
      preview ? `${item.y}px` : `${item.y / 9}%`,
    );
    element.style.setProperty(
      "--drag-height",
      preview ? `${item.h}px` : `${item.h / 9}%`,
    );
  }
}
function finishGesture(event?: PointerEvent, cancel = false) {
  if (!drag || (event && event.pointerId !== drag.pointer)) return;
  if (event && !cancel) move(event);
  cancelAnimationFrame(gestureFrame);
  gestureFrame = 0;
  const completed = drag;
  drag = null;
  for (const entry of completed.elements) {
    entry.element.style.cssText = entry.style;
    delete entry.element.dataset.dragging;
  }
  surface.value?.parentElement?.classList.remove("studio-gesturing");
  if (cancel || props.disabled) return;
  const next = new Map(completed.next.map((item) => [item.id, item]));
  if (
    completed.members.every((item) => {
      const value = next.get(item.id) ?? item;
      return ["x", "y", "w", "h"].every(
        (key) => value[key as "x"] === item[key as "x"],
      );
    })
  )
    return;
  change({
    ...model.value,
    canvas: items.value.map((item) => next.get(item.id) ?? item),
  });
}
function moveSelection(
  members: CanvasWidget[],
  dx: number,
  dy: number,
  record = true,
) {
  const moved = new Map(
    moveCanvasItems(members, dx, dy).map((item) => [item.id, item]),
  );
  change(
    {
      ...model.value,
      canvas: items.value.map((item) => moved.get(item.id) ?? item),
    },
    record,
  );
}
function keyboard(event: KeyboardEvent) {
  if (drag) {
    if (event.key === "Escape") finishGesture(undefined, true);
    return;
  }
  if (event.key === "Escape" && !props.standalone) focused.value = false;
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
  if (!editableSelection.value) return;
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
    moveSelection(selectedItems.value, x * step, y * step);
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
  finishGesture(undefined, true);
  viewportObserver?.disconnect();
  cancelAnimationFrame(resizeFrame);
});
</script>
<template>
  <section
    class="canvas-studio"
    :class="{
      'studio-focused': focused || standalone,
      'studio-page': standalone,
    }"
    @keydown="keyboard"
  >
    <header class="studio-toolbar">
      <div class="studio-heading">
        <a
          v-if="standalone"
          class="studio-back"
          href="/"
          aria-label="返回教师工作台"
          title="返回教师工作台"
          ><ArrowLeft :size="18"
        /></a>
        <span><Layers :size="18" /></span>
        <div>
          <strong>{{ standalone ? classroom.name : "看板设计" }}</strong
          ><small v-if="standalone">看板画布</small>
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
        <button
          v-if="!standalone"
          class="button secondary"
          @click="focused = !focused"
        >
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
          @click="
            playing = false;
            emit('publish', rotationSummary);
          "
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
    <p v-if="disabledReason" class="notice subtle studio-notice">
      {{ disabledReason }}
    </p>
    <div v-if="feedback" class="notice subtle studio-notice" role="status">
      {{ feedback }}
    </div>
    <p v-if="issue" class="notice warning studio-notice" role="alert">
      {{ issue }}
    </p>
    <div class="studio-body" :class="`mobile-panel-${mobilePanel}`">
      <nav v-if="standalone" class="studio-mobile-panels" aria-label="画布工具">
        <button
          :aria-pressed="mobilePanel === 'library'"
          @click="mobilePanel = 'library'"
        >
          添加组件与模板
        </button>
        <button
          :aria-pressed="mobilePanel === 'inspector'"
          @click="mobilePanel = 'inspector'"
        >
          组件属性与外观
        </button>
      </nav>
      <aside class="studio-library">
        <div class="studio-section-title">
          <strong>图层素材</strong><span>{{ items.length }}/24</span>
        </div>
        <StudioPalette :disabled="disabled || items.length >= 24" @add="add" />
        <div v-if="standalone && !classroom.archived" class="studio-template">
          <a
            class="button secondary full"
            :href="`/?notice=${encodeURIComponent(classroom.id)}`"
            target="_blank"
            rel="noopener"
          >
            <Megaphone :size="15" />发送醒目提醒
          </a>
          <p class="studio-help">在教室电脑临时播放跑马灯、弹幕或全屏弹窗。</p>
        </div>
        <div class="studio-template">
          <strong>我的积木 <small>本机</small></strong>
          <p v-if="!pieces.length" class="studio-help">
            选中图层后可保存，随时插入画布。
          </p>
          <div v-for="piece in pieces" :key="piece.id" class="studio-piece-row">
            <button
              type="button"
              class="text-button"
              :disabled="disabled || items.length + piece.items.length > 24"
              :title="piece.name"
              @click="insertItems(piece.items)"
            >
              <Boxes :size="14" />{{ piece.name }}
            </button>
            <button
              type="button"
              class="layer-icon"
              :disabled="disabled"
              :aria-label="'移除积木 ' + piece.name"
              @click="
                storePieces(pieces.filter((entry) => entry.id !== piece.id))
              "
            >
              <Trash2 :size="13" />
            </button>
          </div>
        </div>
        <div class="studio-template">
          <strong>从模板开始</strong
          ><AppSelect
            v-model="template"
            aria-label="选择画布模板"
            :disabled="disabled"
          >
            <option value="standard">课堂标准</option>
            <option value="bento">Bento 分区</option>
            <option value="columns">三栏平分</option>
            <option value="notes">灵感便签</option>
            <option value="blank">空白画布</option></AppSelect
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
            <strong>图层</strong>
            <button
              type="button"
              class="text-button studio-multi-select"
              :aria-pressed="multiSelect"
              @click="multiSelect = !multiSelect"
            >
              多选
            </button>
          </div>
          <div v-if="!items.length" class="studio-help">
            从组件库添加第一块内容
          </div>
          <article
            v-for="item in [...items].reverse()"
            :key="item.id"
            :class="{ active: selection.includes(item.id) }"
          >
            <button
              class="layer-name"
              :aria-pressed="selection.includes(item.id)"
              @click="selectLayer(item.id, $event)"
            >
              <component
                :is="palette.find((entry) => entry.kind === item.kind)?.icon"
                :size="14"
              />
              <span
                >{{ item.title || widgetLabels[item.kind]
                }}<small
                  v-if="item.kind === 'homework' && item.subjectFilter"
                  >{{ item.subjectFilter }}</small
                ></span
              ></button
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
              :page-map="pageMap"
            />
            <div
              ref="surface"
              class="studio-interaction"
              :class="{ snapping: snap }"
              @pointerdown.self="selection = []"
            >
              <div
                v-for="item in items.filter((i) => i.visible)"
                :key="item.id"
                class="studio-object"
                :data-widget-id="item.id"
                :class="{
                  selected: selection.includes(item.id),
                  locked: item.locked,
                }"
                :style="itemStyle(item)"
              >
                <button
                  class="studio-object-drag"
                  :style="
                    item.kind === 'shape'
                      ? shapeStyle(
                          item.shape ?? 'rectangle',
                          0,
                          item.vertices?.length ? item.vertices : undefined,
                        )
                      : undefined
                  "
                  :aria-label="`选择并移动${item.title || widgetLabels[item.kind]}`"
                  @pointerdown="start($event, item, 'move')"
                  @pointermove="move"
                  @pointerup="finishGesture($event)"
                  @pointercancel="finishGesture($event, true)"
                  @lostpointercapture="finishGesture($event, true)"
                  @click="clickCanvas(item.id, $event)"
                ></button
                ><span
                  v-if="current?.id === item.id"
                  class="studio-object-label"
                  >{{ item.title || widgetLabels[item.kind] }} · {{ item.w }}×{{
                    item.h
                  }}</span
                ><template
                  v-if="current?.id === item.id && !item.locked && !disabled"
                  ><button
                    v-for="handle in resizeEdges"
                    :key="handle.edge"
                    class="studio-resize-handle"
                    :class="`handle-${handle.edge}`"
                    :aria-label="`拖动${handle.label}调整${item.title || widgetLabels[item.kind]}`"
                    @pointerdown.stop="start($event, item, handle.edge)"
                    @pointermove="move"
                    @pointerup="finishGesture($event)"
                    @pointercancel="finishGesture($event, true)"
                    @lostpointercapture="finishGesture($event, true)"
                  ></button
                ></template>
              </div>
            </div>
          </div>
        </div>
        <p class="studio-gesture-hint">
          拖动组件移动位置 · 拖动四边或四角调整大小
        </p>
        <div class="studio-page-controls">
          <button
            class="icon-button"
            aria-label="自由画布上一页"
            :disabled="page <= 1"
            @click="previewPage(page - 1)"
          >
            <ChevronLeft :size="16" /></button
          ><span>内容预览 {{ page }} / {{ pages }}</span
          ><button
            class="icon-button"
            aria-label="自由画布下一页"
            :disabled="page >= pages"
            @click="previewPage(page + 1)"
          >
            <ChevronRight :size="16" />
          </button>
          <button
            v-if="pages > 1"
            type="button"
            class="text-button"
            @click="playing = !playing"
          >
            {{ playing ? "暂停轮播预览" : "播放轮播预览" }}
          </button>
        </div>
        <div class="studio-rotation-summary">
          <span role="status">{{ rotationSummary }}</span>
          <button
            v-if="prioritySuggestion"
            type="button"
            class="text-button"
            @click="tryHomeworkPriority"
          >
            试用作业优先布局 · 预计作业 {{ prioritySuggestion.count }} 页
          </button>
          <span v-else-if="homeworkPages >= 5 && !disabled"
            >可放大作业卡片或分时展示</span
          >
        </div>
        <p class="studio-canvas-hint">
          拖动移动 · 右下角缩放 · 方向键微调 · Ctrl+D 复制 · Delete 删除
        </p>
        <div v-if="!items.length" class="studio-empty-hint">
          <Plus :size="20" /><strong>从左侧添加组件</strong>
        </div>
      </div>
      <aside class="studio-inspector">
        <div class="studio-inspector-tabs">
          <button
            :class="{ active: propertyTab === 'component' }"
            :aria-pressed="propertyTab === 'component'"
            @click="propertyTab = 'component'"
          >
            <SlidersHorizontal :size="14" />组件属性</button
          ><button
            :class="{ active: propertyTab === 'background' }"
            :aria-pressed="propertyTab === 'background'"
            @click="propertyTab = 'background'"
          >
            <Palette :size="14" />整体外观
          </button>
        </div>
        <div v-if="propertyTab === 'component'" class="studio-property-body">
          <div v-if="selectedItems.length" class="studio-composition">
            <div
              v-if="selectedItems.length > 1"
              class="studio-selected-heading"
            >
              <div>
                <strong>已选 {{ selectedItems.length }} 个图层</strong>
              </div>
              <button
                type="button"
                class="icon-button"
                :disabled="disabled || items.length + selectedItems.length > 24"
                aria-label="复制选中图层"
                @click="duplicate"
              >
                <Copy :size="16" />
              </button>
              <button
                type="button"
                class="icon-button destructive"
                :disabled="!editableSelection"
                aria-label="删除选中图层"
                @click="remove"
              >
                <Trash2 :size="16" />
              </button>
            </div>
            <div
              v-if="selectedItems.length > 1 || groupedSelection"
              class="studio-composition-actions"
            >
              <button
                v-if="
                  selectedItems.length > 1 &&
                  !selectedItems.every(
                    (item) =>
                      item.groupId &&
                      item.groupId === selectedItems[0]?.groupId,
                  )
                "
                type="button"
                class="button secondary"
                :disabled="!editableSelection"
                @click="groupSelection"
              >
                <Group :size="14" />组合
              </button>
              <button
                v-if="groupedSelection"
                type="button"
                class="button secondary"
                :disabled="!editableSelection"
                @click="ungroupSelection"
              >
                <Ungroup :size="14" />解组
              </button>
              <button
                v-if="current?.groupId"
                type="button"
                class="text-button"
                @click="select(current.id)"
              >
                选中整个组合
              </button>
            </div>
            <div v-if="selectedItems.length > 1" class="studio-member-list">
              <button
                v-for="item in selectedItems"
                :key="item.id"
                type="button"
                class="text-button"
                @click="
                  selection = [item.id];
                  multiSelect = false;
                "
              >
                编辑 {{ item.title || widgetLabels[item.kind] }}
              </button>
            </div>
            <details class="studio-save-piece">
              <summary><Boxes :size="14" />存为我的积木</summary>
              <form class="studio-piece-form" @submit.prevent="savePiece">
                <input
                  v-model="pieceName"
                  aria-label="积木名称"
                  placeholder="为积木起个名字"
                  maxlength="30"
                  :disabled="disabled"
                />
                <button
                  type="submit"
                  class="button secondary"
                  :disabled="disabled || !pieceName.trim()"
                >
                  保存积木
                </button>
              </form>
            </details>
          </div>
          <div v-if="!selectedItems.length" class="studio-no-selection">
            <Layers :size="27" />
            <h3>选择组件以编辑属性</h3>
            <button
              class="text-button"
              :disabled="disabled"
              @click="restoreDraft"
            >
              <FolderOpen :size="14" />读取本机设计草稿
            </button>
          </div>
          <template v-if="current"
            ><div class="studio-selected-heading">
              <div>
                <strong>{{ widgetLabels[current.kind] }}</strong
                ><small v-if="current.locked">已锁定</small>
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
                  ref="textEditor"
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
                />
              </label>
              <label v-if="current.kind === 'text'" class="field"
                >插入班级数据
                <AppSelect value="" @change="insertToken">
                  <option value="">选择要插入的数据</option>
                  <option
                    v-for="token in textTokens"
                    :key="token"
                    :value="token"
                  >
                    {{ token }}
                  </option>
                </AppSelect>
              </label>
              <div
                v-if="current.kind === 'homework'"
                class="studio-content-binding"
              >
                <strong>绑定学科</strong>
                <div class="studio-subject-options" aria-label="绑定学科">
                  <button
                    type="button"
                    :aria-pressed="!current.subjectFilter"
                    @click="bindSubject('')"
                  >
                    全部学科
                  </button>
                  <button
                    v-for="subject in subjectChoices"
                    :key="subject"
                    type="button"
                    :aria-pressed="current.subjectFilter === subject"
                    @click="bindSubject(subject)"
                  >
                    {{ subject }}
                  </button>
                </div>
                <small>{{
                  current.subjectFilter
                    ? `仅显示已发布的${current.subjectFilter}作业`
                    : "显示各学科已发布的作业"
                }}</small>
              </div>
              <template v-if="['message', 'countdown'].includes(current.kind)">
                <div class="field">
                  内容来源
                  <div class="studio-source-segments" aria-label="内容来源">
                    <button
                      type="button"
                      :aria-pressed="current.dataSource !== 'custom'"
                      @click="setDataSource('classroom')"
                    >
                      跟随班级
                    </button>
                    <button
                      type="button"
                      :aria-pressed="current.dataSource === 'custom'"
                      @click="setDataSource('custom')"
                    >
                      独立内容
                    </button>
                  </div>
                </div>
                <template v-if="current.dataSource === 'custom'">
                  <label v-if="current.kind === 'message'" class="field"
                    >寄语内容
                    <textarea
                      :value="current.text"
                      rows="5"
                      maxlength="2000"
                      @focus="checkpoint"
                      @input="
                        patchItem(
                          {
                            text: ($event.target as HTMLTextAreaElement).value,
                          },
                          false,
                        )
                      "
                    />
                  </label>
                  <template v-else>
                    <label class="field"
                      >活动名称<input
                        :value="current.event ?? ''"
                        maxlength="60"
                        @focus="checkpoint"
                        @input="
                          patchItem(
                            {
                              event: ($event.target as HTMLInputElement).value,
                            },
                            false,
                          )
                        "
                    /></label>
                    <label class="field"
                      >目标日期<DatePicker
                        :model-value="current.eventDate ?? ''"
                        :disabled="disabled || current.locked"
                        @update:model-value="patchItem({ eventDate: $event })"
                    /></label>
                  </template>
                </template>
                <p v-else class="studio-source-value">
                  {{
                    current.kind === "message"
                      ? classroom.message || "班级尚未填写寄语"
                      : [classroom.event, classroom.eventDate]
                          .filter(Boolean)
                          .join(" · ") || "班级尚未设置活动"
                  }}
                </p>
              </template>
              <label
                v-if="
                  ['homework', 'todo', 'timetable', 'message'].includes(
                    current.kind,
                  )
                "
                class="studio-checkbox"
              >
                <input
                  type="checkbox"
                  :checked="current.showTitle !== false"
                  @change="
                    patchItem({
                      showTitle: ($event.target as HTMLInputElement).checked,
                    })
                  "
                />
                显示列表标题
              </label>
              <div
                v-if="current.kind === 'shape'"
                class="studio-shape-options"
                aria-label="图形轮廓"
              >
                <button
                  v-for="(label, shape) in shapeLabels"
                  :key="shape"
                  type="button"
                  :aria-label="label"
                  :aria-pressed="(current.shape ?? 'rectangle') === shape"
                  @click="setShape(shape as ShapeKind)"
                >
                  <span
                    :style="shapeStyle(shape as ShapeKind, 3)"
                    aria-hidden="true"
                  ></span
                  >{{ label }}
                </button>
              </div>
              <ShapeSketch
                v-if="current.kind === 'shape' && current.shape === 'custom'"
                :key="current.id"
                :vertices="current.vertices ?? defaultOutline"
                @draw="patchItem({ vertices: $event })"
              />
              <template v-if="current.kind === 'image'"
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
                <p class="studio-help">JPG / PNG / WebP</p></template
              >
              <details class="studio-property-group">
                <summary>位置与尺寸</summary>
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
                      :min="widgetMinimum(current.kind, current.showTitle).w"
                      max="1600"
                      @focus="checkpoint"
                      @input="number('w', $event)"
                      @blur="number('w', $event)" /></label
                  ><label class="field"
                    >高度<input
                      type="number"
                      :value="current.h"
                      :min="widgetMinimum(current.kind, current.showTitle).h"
                      max="900"
                      @focus="checkpoint"
                      @input="number('h', $event)"
                      @blur="number('h', $event)"
                  /></label>
                </div>
                <div class="studio-align">
                  <button type="button" @click="align('x', 0)">靠左</button
                  ><button type="button" @click="align('x', 0.5)">
                    水平居中</button
                  ><button type="button" @click="align('x', 1)">靠右</button
                  ><button type="button" @click="align('y', 0)">靠上</button
                  ><button type="button" @click="align('y', 0.5)">
                    垂直居中</button
                  ><button type="button" @click="align('y', 1)">靠下</button>
                </div>
              </details>
              <template v-if="!['image', 'shape'].includes(current.kind)"
                ><details class="studio-property-group" open>
                  <summary>字体与排版</summary>
                  <div class="studio-type-presets" aria-label="文字样式预设">
                    <button
                      type="button"
                      @click="
                        patchItem({
                          fontFamily: 'sans',
                          fontSize: 32,
                          fontWeight: 400,
                          lineHeight: 1.6,
                        })
                      "
                    >
                      正文
                    </button>
                    <button
                      type="button"
                      @click="
                        patchItem({
                          fontFamily: 'sans',
                          fontSize: 48,
                          fontWeight: 600,
                          lineHeight: 1.3,
                        })
                      "
                    >
                      标题
                    </button>
                    <button
                      type="button"
                      @click="
                        patchItem({
                          fontFamily: 'kai',
                          fontSize: 40,
                          fontWeight: 400,
                          lineHeight: 1.8,
                        })
                      "
                    >
                      寄语
                    </button>
                  </div>
                  <label class="field"
                    >字体样式<AppSelect
                      :value="current.fontFamily ?? 'sans'"
                      @change="
                        patchItem({
                          fontFamily: ($event.target as HTMLSelectElement)
                            .value as CanvasWidget['fontFamily'],
                        })
                      "
                    >
                      <option value="sans">清晰黑体</option>
                      <option value="serif">经典宋体</option>
                      <option value="kai">手写楷体</option>
                    </AppSelect></label
                  >
                  <p
                    class="studio-font-sample"
                    :style="{
                      fontFamily: boardFonts[current.fontFamily ?? 'sans'],
                      fontWeight: current.fontWeight ?? 400,
                    }"
                  >
                    {{ current.text || current.title }}
                  </p>
                  <div class="studio-number-grid">
                    <label class="field"
                      >正文字号<input
                        type="number"
                        min="16"
                        max="96"
                        :value="current.fontSize"
                        @focus="checkpoint"
                        @input="number('fontSize', $event)"
                        @blur="number('fontSize', $event)"
                    /></label>
                    <label
                      v-if="
                        !['text', 'datetime', 'countdown'].includes(
                          current.kind,
                        )
                      "
                      class="field"
                      >标题字号<input
                        type="number"
                        min="16"
                        max="64"
                        :value="current.titleSize ?? 28"
                        @focus="checkpoint"
                        @input="number('titleSize', $event)"
                        @blur="number('titleSize', $event)"
                    /></label>
                    <label class="field"
                      >文字粗细<AppSelect
                        :value="current.fontWeight ?? 400"
                        @change="
                          patchItem({
                            fontWeight: Number(
                              ($event.target as HTMLSelectElement).value,
                            ) as CanvasWidget['fontWeight'],
                          })
                        "
                      >
                        <option :value="400">常规</option>
                        <option :value="500">适中</option>
                        <option :value="600">半粗</option>
                        <option :value="700">加粗</option>
                      </AppSelect></label
                    >
                    <label
                      v-if="!['datetime', 'countdown'].includes(current.kind)"
                      class="field"
                      >行间距<AppSelect
                        :value="current.lineHeight ?? 1.5"
                        @change="
                          patchItem({
                            lineHeight: Number(
                              ($event.target as HTMLSelectElement).value,
                            ),
                          })
                        "
                      >
                        <option :value="1.2">紧凑 · 1.2</option>
                        <option :value="1.3">适中 · 1.3</option>
                        <option :value="1.5">标准 · 1.5</option>
                        <option :value="1.6">舒适 · 1.6</option>
                        <option :value="1.8">宽松 · 1.8</option>
                        <option :value="2">疏朗 · 2.0</option>
                      </AppSelect></label
                    >
                  </div>
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
                            textColor: ($event.target as HTMLInputElement)
                              .value,
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
                    >文字对齐<AppSelect
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
                      <option value="right">右对齐</option>
                    </AppSelect></label
                  >
                </details></template
              >
              <div class="studio-surface-presets" aria-label="卡片风格">
                <strong>卡片风格</strong>
                <div>
                  <button
                    v-for="preset in surfacePresets"
                    :key="preset.key"
                    type="button"
                    :aria-pressed="activeSurface(preset)"
                    @click="patchItem(preset.patch)"
                  >
                    <span
                      class="studio-surface-swatch"
                      aria-hidden="true"
                      :class="`swatch-${preset.key}`"
                      ><span>Aa<span></span><span></span></span
                    ></span>
                    {{ preset.label }}
                  </button>
                </div>
              </div>
              <label class="field"
                >组件底色<AppSelect
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
                </AppSelect></label
              >
              <CardImageControls
                v-if="current.kind !== 'image'"
                :key="current.id"
                :target-id="current.id"
                :image="current.backgroundImage"
                :fit="current.backgroundFit"
                :position="current.backgroundPosition"
                :disabled="disabled || current.locked"
                @update="updateCardImage"
              />
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
                  >{{ current.backgroundImage ? "图片遮罩" : "不透明度"
                  }}<input
                    type="number"
                    min="0"
                    max="100"
                    :value="current.opacity"
                    @focus="checkpoint"
                    @input="number('opacity', $event)"
                    @blur="number('opacity', $event)"
                /></label>
              </div>
              <label
                v-if="
                  current.kind !== 'shape' ||
                  (current.shape ?? 'rectangle') === 'rectangle'
                "
                class="field"
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
              >主题<AppSelect
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
              </AppSelect></label
            ><label class="field"
              >底板材质<AppSelect
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
              </AppSelect></label
            ><BackgroundControls
              :model-value="model"
              :disabled="disabled"
              @update:model-value="change($event)"
            />
            <p class="studio-source-value">
              底板固定铺满显示器，组件可自由排版。
            </p>
            <label class="field"
              >自动翻页间隔<AppSelect
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
              </AppSelect></label
            >
          </fieldset>
          <button
            class="button secondary full"
            :disabled="disabled"
            @click="restoreDraft"
          >
            <FolderOpen :size="15" />读取本机设计草稿
          </button>
          <p class="studio-help">本机草稿 · 未发布</p>
        </div>
      </aside>
    </div>
  </section>
</template>
