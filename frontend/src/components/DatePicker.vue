<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from "vue";
import { CalendarDays, ChevronLeft, ChevronRight } from "@lucide/vue";
const props = withDefaults(
  defineProps<{
    modelValue: string;
    type?: string;
    required?: boolean;
    disabled?: boolean;
  }>(),
  { type: "date" },
);
const emit = defineEmits<{ "update:modelValue": [string]; change: [] }>();
const id = useId(),
  input = ref<HTMLInputElement>(),
  panel = ref<HTMLElement>();
const datetime = computed(() => props.type === "datetime-local");
const text = ref(""),
  chosen = ref(""),
  hour = ref("08"),
  minute = ref("00");
const year = ref(new Date().getFullYear()),
  month = ref(new Date().getMonth());
const pad = (n: number) => String(n).padStart(2, "0");
const iso = (d: Date) =>
  `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => iso(new Date());
watch(
  () => props.modelValue,
  (value) => {
    text.value = value.replace("T", " ");
    input.value?.setCustomValidity("");
  },
  { immediate: true },
);
const years = computed(() =>
  Array.from({ length: 151 }, (_, i) => new Date().getFullYear() - 50 + i),
);
const days = computed(() => {
  const first = new Date(year.value, month.value, 1);
  const offset = (first.getDay() + 6) % 7;
  return Array.from({ length: 42 }, (_, i) => {
    const date = new Date(year.value, month.value, i - offset + 1);
    return {
      key: iso(date),
      day: date.getDate(),
      outside: date.getMonth() !== month.value,
    };
  });
});
function commit(value: string) {
  emit("update:modelValue", value);
  emit("change");
  input.value?.setCustomValidity("");
}
function typed() {
  const value = text.value.trim().replace(" ", "T");
  if (!value) {
    commit("");
    return;
  }
  const match = value.match(
    datetime.value
      ? /^(\d{4})-(\d{2})-(\d{2})T([01]\d|2[0-3]):([0-5]\d)$/
      : /^(\d{4})-(\d{2})-(\d{2})$/,
  );
  if (
    !match ||
    iso(new Date(+match[1]!, +match[2]! - 1, +match[3]!)) !== value.slice(0, 10)
  ) {
    input.value?.setCustomValidity(
      datetime.value
        ? "请输入有效日期时间，例如 2030-12-03 08:00"
        : "请输入有效日期，例如 2030-12-03",
    );
    input.value?.reportValidity();
    return;
  }
  commit(value);
}
function position() {
  if (!panel.value || !input.value) return;
  const rect = input.value.getBoundingClientRect(),
    width = Math.min(320, innerWidth - 24);
  panel.value.style.width = `${width}px`;
  panel.value.style.left = `${Math.max(12, Math.min(rect.left, innerWidth - width - 12))}px`;
  const height = panel.value.offsetHeight || (datetime.value ? 440 : 370);
  panel.value.style.top = `${Math.max(12, Math.min(rect.bottom + 8, innerHeight - height - 12))}px`;
}
function opening(event: Event) {
  if ((event as ToggleEvent).newState !== "open") return;
  chosen.value = props.modelValue.slice(0, 10) || today();
  const d = new Date(chosen.value + "T12:00:00");
  year.value = d.getFullYear();
  month.value = d.getMonth();
  hour.value = props.modelValue.slice(11, 13) || "08";
  minute.value = props.modelValue.slice(14, 16) || "00";
  nextTick(position);
}
function moveMonth(delta: number) {
  const d = new Date(year.value, month.value + delta, 1);
  year.value = d.getFullYear();
  month.value = d.getMonth();
}
function select(day: string) {
  chosen.value = day;
  if (!datetime.value) {
    commit(day);
    panel.value?.hidePopover();
  }
}
function apply() {
  commit(`${chosen.value}T${hour.value}:${minute.value}`);
  panel.value?.hidePopover();
}
async function navigate(event: KeyboardEvent, day: string) {
  const offsets: Record<string, number> = {
    ArrowLeft: -1,
    ArrowRight: 1,
    ArrowUp: -7,
    ArrowDown: 7,
  };
  const delta = offsets[event.key];
  if (delta === undefined) return;
  event.preventDefault();
  const d = new Date(day + "T12:00:00");
  d.setDate(d.getDate() + delta);
  year.value = d.getFullYear();
  month.value = d.getMonth();
  await nextTick();
  panel.value
    ?.querySelector<HTMLButtonElement>(`[data-date="${iso(d)}"]`)
    ?.focus();
}
function scrolled(e: Event) {
  if (
    panel.value?.matches(":popover-open") &&
    !panel.value.contains(e.target as Node)
  )
    position();
}
window.addEventListener("resize", position);
window.addEventListener("scroll", scrolled, true);
onBeforeUnmount(() => {
  window.removeEventListener("resize", position);
  window.removeEventListener("scroll", scrolled, true);
});
</script>

<template>
  <span class="date-field">
    <input
      ref="input"
      v-model="text"
      type="text"
      :required="required"
      :disabled="disabled"
      :placeholder="datetime ? '年-月-日 时:分' : '年-月-日'"
      autocomplete="off"
      @change="typed"
    />
    <button
      type="button"
      class="date-trigger"
      :disabled="disabled"
      :popovertarget="id"
      aria-label="打开日历"
    >
      <CalendarDays :size="18" />
    </button>
    <!-- 原生顶层弹出层不会被抽屉和滚动容器裁切，Esc / 点击外部由浏览器关闭。 -->
    <span
      :id="id"
      ref="panel"
      popover="auto"
      class="date-popover"
      role="dialog"
      aria-label="选择日期"
      @beforetoggle="opening"
      @toggle="position"
      @click.stop
    >
      <span class="date-heading">
        <button type="button" aria-label="上个月" @click="moveMonth(-1)">
          <ChevronLeft :size="18" />
        </button>
        <AppSelect v-model.number="year" aria-label="年份">
          <option v-for="y in years" :key="y" :value="y">{{ y }} 年</option>
        </AppSelect>
        <AppSelect v-model.number="month" aria-label="月份">
          <option v-for="m in 12" :key="m" :value="m - 1">{{ m }} 月</option>
        </AppSelect>
        <button type="button" aria-label="下个月" @click="moveMonth(1)">
          <ChevronRight :size="18" />
        </button>
      </span>
      <span class="date-week"
        ><span
          v-for="w in ['一', '二', '三', '四', '五', '六', '日']"
          :key="w"
          >{{ w }}</span
        ></span
      >
      <span class="date-days" aria-label="日期">
        <button
          v-for="d in days"
          :key="d.key"
          type="button"
          :data-date="d.key"
          :aria-label="d.key"
          :aria-pressed="chosen === d.key"
          :aria-current="d.key === today() ? 'date' : undefined"
          :class="{
            outside: d.outside,
            chosen: chosen === d.key,
            today: d.key === today(),
          }"
          @click="select(d.key)"
          @keydown="navigate($event, d.key)"
        >
          {{ d.day }}
        </button>
      </span>
      <span v-if="datetime" class="date-time"
        ><span>时间</span
        ><AppSelect v-model="hour" aria-label="小时">
          <option v-for="h in 24" :key="h" :value="pad(h - 1)">
            {{ pad(h - 1) }}
          </option></AppSelect
        ><span>:</span
        ><AppSelect v-model="minute" aria-label="分钟">
          <option v-for="m in 60" :key="m" :value="pad(m - 1)">
            {{ pad(m - 1) }}
          </option>
        </AppSelect></span
      >
      <span class="date-footer"
        ><button
          v-if="!required"
          type="button"
          @click="
            commit('');
            panel?.hidePopover();
          "
        >
          清除</button
        ><button
          type="button"
          @click="
            year = new Date().getFullYear();
            month = new Date().getMonth();
            select(today());
          "
        >
          今天</button
        ><button
          v-if="datetime"
          type="button"
          class="date-apply"
          @click="apply"
        >
          确定
        </button></span
      >
    </span>
  </span>
</template>

<style scoped>
.date-field {
  position: relative;
  display: flex;
  width: 100%;
  min-width: 0;
}
.date-field > input {
  width: 100%;
  padding-right: 44px;
  font-variant-numeric: tabular-nums;
}
.date-trigger {
  position: absolute;
  right: 5px;
  top: 50%;
  transform: translateY(-50%);
  width: 34px;
  height: 34px;
  display: grid;
  place-items: center;
  border: 0;
  border-radius: 8px;
  color: #64748b;
  background: transparent;
  cursor: pointer;
}
.date-popover {
  position: fixed;
  inset: auto;
  margin: 0;
  padding: 18px;
  max-height: calc(100dvh - 24px);
  overflow: auto;
  border: 1px solid #e2e8f0;
  border-radius: 20px;
  box-shadow:
    0 16px 60px #15233b26,
    0 2px 8px #15233b0a;
  background: #fff;
  color: #253349;
  font-family: inherit;
  font-size: 14px;
  line-height: 1.4;
  font-weight: 400;
  box-sizing: border-box;
}
.date-popover button {
  border: 0;
  background: transparent;
  color: inherit;
  border-radius: 10px;
  padding: 0;
  cursor: pointer;
  font: inherit;
}
.date-popover button:hover,
.date-trigger:hover {
  background: #f0f4fa;
}
.date-popover button:focus-visible {
  outline: 2px solid #507bc9;
  outline-offset: 1px;
}
.date-heading {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 18px;
}
.date-heading > button {
  display: grid;
  place-items: center;
  width: 30px;
  height: 34px;
  flex-shrink: 0;
}
.date-popover select {
  background: #f5f7fa;
  color: #28364e;
  border: 0;
  border-radius: 8px;
  height: 34px;
  padding: 0 7px;
  font: inherit;
  min-width: 0;
  width: auto;
}
.date-heading > select {
  flex: 1;
  font-weight: 650;
}
.date-week,
.date-days {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: 4px;
}
.date-week {
  color: #8993a3;
  text-align: center;
  font-size: 12px;
  margin-bottom: 9px;
}
.date-days > button {
  height: 34px;
  font-variant-numeric: tabular-nums;
}
.date-days > .outside {
  color: #bdc4cf;
}
.date-days > .today {
  box-shadow: inset 0 0 0 1px #cddbef;
}
.date-days > .chosen {
  background: #356bd4;
  color: white;
  box-shadow: 0 3px 8px #356bd42b;
}
.date-time {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 12px;
}
.date-time > span:first-child {
  margin-right: auto;
  color: #7c8798;
}
.date-footer {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid #edf0f5;
}
.date-footer > button {
  padding: 7px 12px;
  font-size: 13px;
  color: #54709c;
}
.date-footer > .date-apply {
  margin-left: auto;
  background: #356bd4;
  color: #fff;
}
</style>
