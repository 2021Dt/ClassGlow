<script setup lang="ts">
import DatePicker from "./DatePicker.vue";
import {
  computed,
  nextTick,
  onMounted,
  onUnmounted,
  reactive,
  ref,
  watch,
} from "vue";
import { X, Eye, Send, Check, Save, ChevronLeft } from "@lucide/vue";
import { model, clone, localSave } from "../store";
import {
  canPublish,
  addDays,
  dateKey,
  defaultUntil,
  isAdmin,
  localInput,
  timeLabel,
  toInstant,
  uid,
  type Draft,
} from "../domain";
import {
  api,
  ApiError,
  premiumAvailable,
  localDraftKey,
  persistInput,
  removeInput,
  saveCloudDraft,
  session,
} from "../api";

const props = defineProps<{ draft: Draft }>();
const emit = defineEmits<{ close: []; published: [number] }>();
const dialog = ref<HTMLDialogElement>();
const form = reactive(clone(props.draft));
const fromInput = ref(localInput(form.from)),
  untilInput = ref(localInput(form.until)),
  dueInput = ref(form.due ? localInput(form.due) : "");
const saveState = ref("尚未保存"),
  error = ref(""),
  preview = ref(false),
  busy = ref(false);
const conflict = ref<"draft" | "content" | "">(""),
  latest = ref<any>(null);
const closeChoice = ref(false);
const friday = computed(
  () => new Date(`${dateKey(form.from)}T12:00:00+08:00`).getUTCDay() === 5,
);
let timer: ReturnType<typeof setTimeout> | undefined;
let saveChain: Promise<boolean> = Promise.resolve(true);
let lastSavedPayload = "";
const pendingPublish = ref<{
  key: string;
  draftId: string;
  draftRevision: number;
} | null>(null);
try {
  pendingPublish.value = JSON.parse(
    localStorage.getItem(`${localDraftKey(form.id)}-publish`) ?? "null",
  );
} catch {
  /* 损坏副本不覆盖正文。 */
}
let endTouched =
  Date.parse(form.until) !== Date.parse(defaultUntil(form.from, form.kind));
const max = computed(() => (form.kind === "homework" ? 500 : 200));
const length = computed(() => Array.from(form.body).length);
const subjects = computed(() => [
  ...new Set(model.classes.flatMap((c) => c.authorizedSubjects ?? c.subjects)),
]);
const invalidTargets = computed(() =>
  form.classIds.flatMap((id) => {
    const c = model.classes.find((c) => c.id === id);
    return !c || !canPublish(c, form.subject)
      ? [{ id, name: c?.name ?? "已失去访问的班级" }]
      : [];
  }),
);
const missingTargets = computed(() =>
  form.classIds.filter((id) => !model.classes.some((c) => c.id === id)),
);
const source = computed(() =>
  model.contents.find((c) => c.id === form.sourceId),
);
const payload = () =>
  JSON.stringify([
    form.classIds,
    form.subject,
    form.body,
    form.from,
    form.until,
    form.due,
    form.reason,
    form.priority,
  ]);
function flush(): Promise<boolean> {
  if (timer) clearTimeout(timer);
  // 自动保存串行化，避免自己的两次请求竞争修订；发布等待队列并补存最后输入。
  saveChain = saveChain.then(async () => {
    if (pendingPublish.value || conflict.value) return false;
    const signature = payload();
    if (signature === lastSavedPayload && form.revision > 0) return true;
    saveState.value = "正在同步草稿…";
    try {
      persistInput(clone(form));
      const saved = await saveCloudDraft(clone(form));
      form.revision = saved.revision;
      form.updated = saved.updated;
      lastSavedPayload = signature;
      if (payload() === signature) removeInput(form.id);
      saveState.value = `草稿已同步 ${timeLabel(saved.updated)}`;
      error.value = "";
      return true;
    } catch (e) {
      error.value = (e as Error).message;
      saveState.value = "仅保存在当前设备 · 未同步";
      if (e instanceof ApiError && e.code === "draft_revision_conflict")
        conflict.value = "draft";
      return false;
    }
  });
  return saveChain;
}
watch(
  payload,
  () => {
    try {
      persistInput(clone(form));
      localSave.failed = false;
    } catch {
      localSave.failed = true;
      error.value = "浏览器本机保存失败，请勿关闭页面，并复制正文备份。";
    }
    saveState.value = "等待同步草稿…";
    if (timer) clearTimeout(timer);
    timer = setTimeout(flush, 2000);
  },
  { immediate: true },
);
function chooseWeekend() {
  endTouched = true;
  form.until = `${addDays(dateKey(form.from), 3)}T08:00:00+08:00`;
  untilInput.value = localInput(form.until);
}
function changeFrom() {
  if (!fromInput.value) return;
  form.from = toInstant(fromInput.value);
  if (!endTouched) {
    form.until = defaultUntil(form.from, form.kind);
    untilInput.value = localInput(form.until);
  }
}
function changeUntil() {
  endTouched = true;
  if (untilInput.value) form.until = toInstant(untilInput.value);
}
async function close() {
  if (busy.value) return;
  if (pendingPublish.value || (await flush())) emit("close");
  else closeChoice.value = true;
}
function leaveWithLocal() {
  try {
    persistInput(clone(form));
    emit("close");
  } catch {
    error.value = "本机保存失败，请复制正文备份后再离开。";
  }
}
function online() {
  if (!conflict.value && !pendingPublish.value) void flush();
}
async function publish() {
  busy.value = true;
  await nextTick();
  try {
    if (!fromInput.value || !untilInput.value)
      throw new Error("请完整填写开始展示与结束展示时间。");
    if (!pendingPublish.value) {
      if (!(await flush())) return;
      pendingPublish.value = {
        key: uid(),
        draftId: form.id,
        draftRevision: form.revision,
      };
      localStorage.setItem(
        `${localDraftKey(form.id)}-publish`,
        JSON.stringify(pendingPublish.value),
      );
    }
    const request = pendingPublish.value;
    const result = await api(
      "/publish-batches",
      "POST",
      { draftId: request.draftId, draftRevision: request.draftRevision },
      request.key,
    );
    finishPublished(result.results.length);
  } catch (e) {
    error.value = (e as Error).message;
    if (
      e instanceof ApiError &&
      (e.code === "content_revision_conflict" ||
        e.code === "draft_revision_conflict")
    )
      conflict.value =
        e.code === "content_revision_conflict" ? "content" : "draft";
    if (e instanceof ApiError && e.status > 0) {
      pendingPublish.value = null;
      localStorage.removeItem(`${localDraftKey(form.id)}-publish`);
    }
  } finally {
    busy.value = false;
  }
}
function finishPublished(count: number) {
  try {
    removeInput(form.id);
    localStorage.removeItem(`${localDraftKey(form.id)}-publish`);
    for (const id of form.classIds)
      localStorage.setItem(
        `kanban-subject-${session.userId}-${id}`,
        form.subject,
      );
  } catch {
    /* 正式发布已确认，本机偏好清理失败不能变成重复发布。 */
  }
  emit("published", count);
}
async function queryPublish() {
  try {
    const result = await api(`/publish-batches/${pendingPublish.value!.key}`);
    finishPublished(result.results.length);
  } catch (e) {
    error.value = (e as Error).message;
  }
}
async function compareLatest() {
  try {
    latest.value = await api(
      conflict.value === "content" && form.sourceId
        ? `/contents/${form.sourceId}`
        : `/drafts/${form.id}`,
    );
  } catch (e) {
    error.value = (e as Error).message;
  }
}
function rebase(copyAsNew = false) {
  if (!latest.value) return;
  // 比较后由本人明确选择保留本次正文；只推进基线修订，不替换未提交的输入。
  if (conflict.value === "draft") form.revision = latest.value.revision;
  else if (copyAsNew) {
    form.sourceId = undefined;
    form.baseRevision = undefined;
    form.reason = "";
    form.from = new Date().toISOString();
    form.until = defaultUntil(form.from, form.kind);
    form.due = "";
    fromInput.value = localInput(form.from);
    untilInput.value = localInput(form.until);
    dueInput.value = "";
  } else form.baseRevision = latest.value.revision;
  conflict.value = "";
  latest.value = null;
  lastSavedPayload = "";
  error.value = "";
  try {
    persistInput(clone(form));
    saveState.value = "已保留本次输入，请核对后重新发布";
  } catch {
    error.value = "本机保存失败，请复制正文备份并保持此页打开。";
  }
}
onMounted(() => {
  dialog.value?.showModal();
  window.addEventListener("online", online);
});
onUnmounted(() => {
  window.removeEventListener("online", online);
  if (timer) clearTimeout(timer);
});
</script>

<template>
  <dialog ref="dialog" class="editor-dialog" @cancel.prevent="close">
    <form class="editor-form" @submit.prevent="publish">
      <header class="sheet-header">
        <div>
          <p class="eyebrow">
            {{ form.sourceId ? "修改已发布内容" : "发布到班级" }}
          </p>
          <h2>
            {{
              preview
                ? "发布前预览"
                : `${form.sourceId ? "修改" : "发布"}${form.kind === "homework" ? "作业" : "提醒"}`
            }}
          </h2>
        </div>
        <button
          type="button"
          class="icon-button"
          aria-label="保存草稿并关闭"
          @click="close"
        >
          <X :size="22" />
        </button>
      </header>
      <div class="sheet-body">
        <fieldset class="editor-fields" :disabled="busy || !!pendingPublish">
          <template v-if="!preview">
            <details
              class="compose-targets"
              :open="!form.classIds.length || !!invalidTargets.length"
            >
              <summary>
                <span>发布班级</span
                ><strong>{{
                  model.classes
                    .filter((c) => form.classIds.includes(c.id))
                    .map((c) => c.name)
                    .join("、") || "请选择班级"
                }}</strong
                ><small>更改</small>
              </summary>
              <fieldset class="target-field">
                <legend>发布到 <span class="required">*</span></legend>
                <div class="class-checks">
                  <label
                    v-for="c in model.classes"
                    :key="c.id"
                    :class="{ selected: form.classIds.includes(c.id) }"
                    ><input
                      type="checkbox"
                      :value="c.id"
                      v-model="form.classIds"
                      :disabled="
                        !!form.sourceId ||
                        (!premiumAvailable() &&
                          form.classIds.length >= 1 &&
                          !form.classIds.includes(c.id))
                      "
                    />{{ c.name }}</label
                  ><label
                    v-for="id in missingTargets"
                    :key="id"
                    class="invalid-target"
                    ><input
                      type="checkbox"
                      :value="id"
                      v-model="form.classIds"
                      :disabled="!!form.sourceId"
                    />失效目标 · 取消选择后可继续</label
                  >
                </div>
                <small v-if="!premiumAvailable() && model.classes.length > 1"
                  >多班发布需高级许可</small
                >
              </fieldset>
            </details>
            <label class="field"
              >{{ form.kind === "homework" ? "科目" : "提醒范围" }}
              <AppSelect v-model="form.subject">
                <option v-if="form.kind === 'todo'" value="公共提醒">
                  公共提醒
                </option>
                <option v-for="s in subjects" :key="s">{{ s }}</option>
              </AppSelect></label
            >
            <p v-if="invalidTargets.length" class="field-error" role="alert">
              {{
                invalidTargets.map((c) => c.name).join("、")
              }}未授权此科目，请调整目标或科目。
            </p>
            <label class="field"
              >{{ form.kind === "homework" ? "作业内容" : "提醒内容" }}
              <span class="required">*</span
              ><textarea
                v-model="form.body"
                :rows="7"
                :placeholder="
                  form.kind === 'homework' ? '填写作业内容' : '填写提醒内容'
                "
                autofocus
              />
            </label>
            <div class="editor-caption">
              <span :class="{ 'field-error': length > max }"
                >{{ length }} / {{ max }} 字</span
              >
            </div>
            <label v-if="form.kind === 'todo'" class="priority-option"
              ><input
                type="checkbox"
                v-model="form.priority"
                :disabled="
                  !form.classIds.every((id) =>
                    isAdmin(model.classes.find((c) => c.id === id)),
                  )
                "
              />高优先级提醒 <small>每班最多 3 条</small></label
            >
            <details class="compose-schedule">
              <summary>
                <span>展示至 {{ timeLabel(form.until, true) }}</span
                ><small>修改时间与截止日期</small>
              </summary>
              <h3 class="form-subheading">展示时间</h3>
              <button
                v-if="friday && form.kind === 'homework'"
                type="button"
                class="text-button"
                @click="chooseWeekend"
              >
                周末作业，展示至下周一 08:00
              </button>
              <p class="muted small">北京时间 · 展示以“结束展示”为准</p>
              <div class="form-grid">
                <label class="field"
                  >开始展示<DatePicker
                    type="datetime-local"
                    v-model="fromInput"
                    required
                    @change="changeFrom" /></label
                ><label class="field"
                  >结束展示<DatePicker
                    type="datetime-local"
                    v-model="untilInput"
                    required
                    @change="changeUntil"
                /></label>
              </div>
              <label class="field"
                >{{ form.kind === "homework" ? "提交截止" : "活动时间"
                }}<span class="optional">选填</span
                ><DatePicker
                  type="datetime-local"
                  v-model="dueInput"
                  @change="form.due = dueInput ? toInstant(dueInput) : ''"
              /></label>
              <p
                v-if="form.due && new Date(form.due) < new Date(form.from)"
                class="notice warning"
              >
                截止时间早于开始展示，请核对是否符合安排。
              </p>
            </details>
            <label
              v-if="source && source.authorId !== session.userId"
              class="field"
              >代改原因 <span class="required">*</span
              ><input
                v-model="form.reason"
                maxlength="100"
                placeholder="填写修改原因"
            /></label>
          </template>
          <template v-else>
            <div class="preview-target">
              <span class="badge">预览 · 尚未发布</span>
              <h3>
                {{
                  model.classes
                    .filter((c) => form.classIds.includes(c.id))
                    .map((c) => c.name)
                    .join("、") || "未选择班级"
                }}
              </h3>
            </div>
            <article class="editor-content-preview">
              <span class="subject-tag">{{ form.subject }}</span>
              <p>{{ form.body || "请先填写内容" }}</p>
            </article>
            <dl class="detail-list">
              <div>
                <dt>开始展示</dt>
                <dd>{{ timeLabel(form.from, true) }}</dd>
              </div>
              <div>
                <dt>结束展示</dt>
                <dd>{{ timeLabel(form.until, true) }}</dd>
              </div>
              <div v-if="form.due">
                <dt>
                  {{ form.kind === "homework" ? "提交截止" : "活动时间" }}
                </dt>
                <dd>{{ timeLabel(form.due, true) }}</dd>
              </div>
            </dl>
            <p class="notice subtle">发布后，已连接的教室电脑会自动同步。</p>
          </template>
        </fieldset>
        <p v-if="error" class="field-error error-box" role="alert">
          {{ error }}
        </p>
        <div v-if="closeChoice" class="notice warning">
          <span>草稿尚未同步。保留在本机后可稍后继续，联网不会自动发布。</span
          ><button type="button" class="text-button" @click="leaveWithLocal">
            保留本机并返回</button
          ><button
            type="button"
            class="text-button"
            @click="closeChoice = false"
          >
            继续编辑
          </button>
        </div>
        <section v-if="conflict" class="conflict-panel">
          <h3>内容已更新</h3>
          <p class="muted small">请核对最新内容后继续。</p>
          <button class="button secondary" type="button" @click="compareLatest">
            读取最新版本并比较
          </button>
          <div v-if="latest" class="conflict-columns">
            <article>
              <strong>本次未提交内容</strong>
              <small
                >{{ form.subject }} · {{ timeLabel(form.from, true) }} 至
                {{ timeLabel(form.until, true) }}</small
              >
              <p>{{ form.body }}</p>
            </article>
            <article>
              <strong>最新版本 · 修订 {{ latest.revision }}</strong>
              <small
                >{{ latest.subject }} · {{ timeLabel(latest.from, true) }} 至
                {{ timeLabel(latest.until, true) }}</small
              >
              <p>{{ latest.body }}</p>
            </article>
          </div>
          <button
            v-if="
              latest && (conflict === 'draft' || latest.state === 'published')
            "
            class="button secondary"
            type="button"
            @click="rebase()"
          >
            已比较，以最新修订继续并保留我的正文</button
          ><button
            v-else-if="latest"
            class="button secondary"
            type="button"
            @click="rebase(true)"
          >
            原记录已关闭，复制为新内容并保留我的正文
          </button>
        </section>
        <button
          v-if="pendingPublish"
          type="button"
          class="button secondary"
          @click="queryPublish"
        >
          查询原发布结果
        </button>
      </div>
      <footer class="sheet-footer">
        <p class="draft-save" aria-live="polite">
          <Check :size="16" v-if="saveState.startsWith('草稿已同步')" /><Save
            :size="16"
            v-else
          />{{ saveState }}
        </p>
        <div class="sheet-actions">
          <button
            class="button secondary"
            type="button"
            @click="preview = !preview"
          >
            <ChevronLeft v-if="preview" :size="17" /><Eye v-else :size="17" />{{
              preview ? "返回编辑" : "预览"
            }}</button
          ><button
            class="button primary"
            :disabled="
              busy ||
              !form.body.trim() ||
              !!invalidTargets.length ||
              !form.classIds.length ||
              length > max
            "
            type="submit"
          >
            <Send :size="17" />{{
              busy
                ? "正在提交…"
                : pendingPublish
                  ? "使用原请求重试"
                  : form.classIds.length > 1
                    ? `发布到 ${form.classIds.length} 个班`
                    : "确认发布"
            }}
          </button>
        </div>
      </footer>
    </form>
  </dialog>
</template>
