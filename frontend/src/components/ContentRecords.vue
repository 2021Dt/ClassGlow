<script setup lang="ts">
import DatePicker from "./DatePicker.vue";
import TaskGuide from "./TaskGuide.vue";
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { ChevronLeft, ChevronRight, RefreshCw, Search } from "@lucide/vue";
import { api, session } from "../api";
import { readRecordFilters, recordFiltersSearch } from "../workspaceRoute";
import {
  isAdmin,
  statusLabel,
  timeLabel,
  type Classroom,
  type Content,
} from "../domain";

const props = defineProps<{ classroom: Classroom; now: number }>();
const emit = defineEmits<{
  edit: [Content];
  history: [Content];
  action: [Content, string];
}>();
const initialFilters = readRecordFilters(location.search);
const kind = ref(initialFilters.kind),
  state = ref(initialFilters.state),
  subject = ref(initialFilters.subject),
  date = ref(initialFilters.date),
  search = ref(initialFilters.search),
  offset = ref(initialFilters.offset);
const items = ref<Content[]>([]),
  total = ref(0),
  error = ref(""),
  loading = ref(false);
const writable = computed(() => !props.classroom.archived);
const page = computed(() => Math.floor(offset.value / 20) + 1);
let serial = 0,
  timer: ReturnType<typeof setTimeout>;
let restoringFilters = false;
function syncFilters(push = false) {
  const next = recordFiltersSearch(location.search, {
    kind: kind.value,
    state: state.value,
    subject: subject.value,
    date: date.value,
    search: search.value,
    offset: offset.value,
  });
  if (next !== location.search)
    history[push ? "pushState" : "replaceState"](null, "", "/" + next);
}
function restoreFilters() {
  clearTimeout(timer);
  restoringFilters = true;
  const filters = readRecordFilters(location.search);
  kind.value = filters.kind;
  state.value = filters.state;
  subject.value = filters.subject;
  date.value = filters.date;
  search.value = filters.search;
  offset.value = filters.offset;
  restoringFilters = false;
  void load();
}
async function load() {
  const ticket = ++serial;
  loading.value = true;
  error.value = "";
  const params = new URLSearchParams({
    classId: props.classroom.id,
    kind: kind.value,
    state: state.value,
    subject: subject.value,
    date: date.value,
    search: search.value,
    offset: String(offset.value),
    limit: "20",
  });
  try {
    const result = await api(`/contents?${params}`);
    if (ticket === serial) {
      items.value = result.items;
      total.value = result.total;
      if (offset.value > 0 && offset.value >= total.value)
        offset.value = Math.max(0, Math.ceil(total.value / 20) - 1) * 20;
    }
  } catch (e) {
    if (ticket === serial) error.value = (e as Error).message;
  } finally {
    if (ticket === serial) loading.value = false;
  }
}
watch(
  [kind, state, subject, date, search],
  () => {
    if (restoringFilters) return;
    clearTimeout(timer);
    timer = setTimeout(() => {
      restoringFilters = true;
      offset.value = 0;
      restoringFilters = false;
      syncFilters();
      load();
    }, 300);
  },
  { flush: "sync" },
);
watch(
  () => props.classroom.id,
  () => {
    restoringFilters = true;
    subject.value = "";
    offset.value = 0;
    restoringFilters = false;
    syncFilters();
    load();
  },
);
watch(
  offset,
  () => {
    if (!restoringFilters) {
      syncFilters(true);
      void load();
    }
  },
  { flush: "sync" },
);
onMounted(() => {
  window.addEventListener("popstate", restoreFilters);
  void load();
});
onUnmounted(() => {
  clearTimeout(timer);
  window.removeEventListener("popstate", restoreFilters);
  serial++;
});
defineExpose({ reload: load });
const mayEdit = (c: Content) =>
  writable.value && (isAdmin(props.classroom) || c.authorId === session.userId);
</script>

<template>
  <section class="panel">
    <TaskGuide
      name="content-records"
      title="查找、修改和撤回已发布内容"
      :steps="[
        {
          title: '按状态与日期查找',
          text: '“展示中”是当前内容，“待展示”会在开始时间自动出现。可组合科目、日期和关键词筛选。',
        },
        {
          title: '修改或停止展示',
          text: '有权限的内容可编辑、完成或撤回；操作后同步到该班级电脑。查看历史可核对修改前后的内容。',
        },
        {
          title: '需要清理时再删除',
          text: '先确认内容已结束或撤回，再使用可用的删除入口。永久删除无法恢复，操作前核对目标。',
        },
      ]"
    />
    <div class="record-filters">
      <label class="field"
        >内容类型<AppSelect v-model="kind">
          <option value="">全部</option>
          <option value="homework">作业</option>
          <option value="todo">提醒</option>
        </AppSelect></label
      ><label class="field"
        >状态<AppSelect v-model="state">
          <option value="">全部状态</option>
          <option value="active">展示中</option>
          <option value="scheduled">待展示</option>
          <option value="ended">展示结束</option>
          <option value="completed">已完成</option>
          <option value="withdrawn">已撤回</option>
        </AppSelect></label
      ><label class="field"
        >学科<AppSelect v-model="subject">
          <option value="">全部学科</option>
          <option value="公共提醒">公共提醒</option>
          <option v-for="s in classroom.subjects" :key="s">{{ s }}</option>
        </AppSelect></label
      ><label class="field"
        >某日展示范围<DatePicker type="date" v-model="date" /></label
      ><label class="search-field"
        ><Search :size="17" /><input
          v-model="search"
          placeholder="搜索正文"
          aria-label="搜索正式内容" /></label
      ><button class="icon-button" aria-label="刷新内容记录" @click="load">
        <RefreshCw :size="18" />
      </button>
    </div>
    <p class="muted small">共 {{ total }} 条正式记录 · 私人草稿不在本列表</p>
    <p v-if="error" class="notice danger" role="alert">{{ error }}</p>
    <p v-if="!items.length" class="empty-state">
      {{ loading ? "正在读取正式记录…" : "没有符合条件的正式记录。" }}
    </p>
    <div
      class="bounded-record-list"
      role="region"
      aria-label="正式内容列表，可滚动查看"
      tabindex="0"
    >
      <article v-for="c in items" :key="c.id" class="record-row">
        <div class="record-top">
          <span class="subject-tag">{{ c.subject }}</span
          ><span v-if="c.priority" class="badge amber-badge">高优先级</span
          ><span class="badge neutral">{{ statusLabel(c, now) }}</span
          ><span class="record-meta"
            >{{ c.author }} · {{ timeLabel(c.created, true) }} · 修订
            {{ c.revision }}</span
          >
        </div>
        <p class="record-body">{{ c.body }}</p>
        <p class="record-meta">
          展示 {{ timeLabel(c.from, true) }} 至 {{ timeLabel(c.until, true)
          }}<span v-if="c.due">
            · {{ c.kind === "homework" ? "提交截止" : "活动时间" }}
            {{ timeLabel(c.due, true) }}</span
          ><span v-if="c.reason"> · 管理员代改：{{ c.reason }}</span>
        </p>
        <div class="record-actions">
          <button class="text-button" @click="emit('history', c)">
            修改历史</button
          ><button
            class="text-button"
            :disabled="c.state === 'published' ? !mayEdit(c) : !writable"
            @click="emit('edit', c)"
          >
            {{ c.state === "published" ? "编辑" : "复制为新内容" }}</button
          ><button
            v-if="c.kind === 'todo' && c.state !== 'withdrawn'"
            class="text-button"
            :disabled="!mayEdit(c)"
            @click="
              emit(
                'action',
                c,
                c.state === 'completed' ? 'restore' : 'complete',
              )
            "
          >
            {{ c.state === "completed" ? "恢复未完成" : "标记完成" }}</button
          ><button
            v-if="c.state === 'published'"
            class="text-button destructive"
            :disabled="!mayEdit(c)"
            @click="emit('action', c, 'withdraw')"
          >
            撤回
          </button>
          <button
            class="text-button destructive"
            :disabled="!mayEdit(c)"
            @click="emit('action', c, 'delete')"
          >
            删除
          </button>
        </div>
      </article>
    </div>
    <div class="records-pagination">
      <button
        class="button secondary"
        :disabled="offset === 0 || loading"
        @click="offset = Math.max(0, offset - 20)"
      >
        <ChevronLeft :size="16" />上一页</button
      ><span>第 {{ page }} / {{ Math.max(1, Math.ceil(total / 20)) }} 页</span
      ><button
        class="button secondary"
        :disabled="offset + 20 >= total || loading"
        @click="offset += 20"
      >
        下一页<ChevronRight :size="16" />
      </button>
    </div>
  </section>
</template>
