<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { ChevronLeft, ChevronRight, RefreshCw, Search } from "@lucide/vue";
import { api, session } from "../api";
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
const kind = ref(""),
  state = ref(""),
  subject = ref(""),
  date = ref(""),
  search = ref(""),
  offset = ref(0);
const items = ref<Content[]>([]),
  total = ref(0),
  error = ref(""),
  loading = ref(false);
const writable = computed(() => !props.classroom.archived);
const page = computed(() => Math.floor(offset.value / 20) + 1);
let serial = 0,
  timer: ReturnType<typeof setTimeout>;
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
    }
  } catch (e) {
    if (ticket === serial) error.value = (e as Error).message;
  } finally {
    if (ticket === serial) loading.value = false;
  }
}
watch([kind, state, subject, date, search], () => {
  clearTimeout(timer);
  timer = setTimeout(() => {
    offset.value = 0;
    load();
  }, 300);
});
watch(
  () => props.classroom.id,
  () => {
    subject.value = "";
    offset.value = 0;
    load();
  },
);
watch(offset, load);
onMounted(load);
onUnmounted(() => {
  clearTimeout(timer);
  serial++;
});
defineExpose({ reload: load });
const mayEdit = (c: Content) =>
  writable.value && (isAdmin(props.classroom) || c.authorId === session.userId);
</script>

<template>
  <section class="panel">
    <div class="record-filters">
      <label class="field"
        >内容类型<select v-model="kind">
          <option value="">全部</option>
          <option value="homework">作业</option>
          <option value="todo">提醒</option>
        </select></label
      ><label class="field"
        >状态<select v-model="state">
          <option value="">全部状态</option>
          <option value="active">展示中</option>
          <option value="scheduled">待展示</option>
          <option value="ended">展示结束</option>
          <option value="completed">已完成</option>
          <option value="withdrawn">已撤回</option>
        </select></label
      ><label class="field"
        >学科<select v-model="subject">
          <option value="">全部学科</option>
          <option value="公共提醒">公共提醒</option>
          <option v-for="s in classroom.subjects" :key="s">{{ s }}</option>
        </select></label
      ><label class="field"
        >某日展示范围<input type="date" v-model="date" /></label
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
