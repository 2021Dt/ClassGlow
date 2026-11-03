<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { CalendarDays, Copy, Plus, Trash2 } from "@lucide/vue";
import { api } from "../api";
import { addDays, dateKey, isAdmin, type Classroom } from "../domain";

type Period = { start: string; end: string; subject: string };
const props = defineProps<{ classroom: Classroom }>();
const emit = defineEmits<{ refresh: [] }>();
const allowed = computed(
  () => isAdmin(props.classroom) && !props.classroom.archived,
);
const weekdays = reactive<Record<string, Period[]>>({});
const selectedDay = ref("1"),
  from = ref(dateKey()),
  until = ref(addDays(dateKey(), 120)),
  revision = ref(1);
const error = ref(""),
  notice = ref(""),
  busy = ref(false);
const override = reactive({
  date: dateKey(),
  mode: "closed",
  weekday: 1,
  periods: [] as Period[],
});
const weekdayNames = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
const viewingTemplate = ref(""),
  viewingDay = ref("1");
const publishedTemplate = computed(
  () =>
    props.classroom.templates?.find(
      (t) => t.effectiveFrom === viewingTemplate.value,
    ) ??
    props.classroom.templates?.find(
      (t) => t.effectiveFrom <= dateKey() && dateKey() <= t.effectiveUntil,
    ) ??
    props.classroom.templates?.at(-1),
);
const publishedPeriods = computed(
  () => publishedTemplate.value?.weekdays[viewingDay.value] ?? [],
);
const selectedPeriods = computed(() => weekdays[selectedDay.value] ?? []);
function init() {
  for (let i = 1; i <= 7; i++) weekdays[String(i)] = [];
  revision.value = props.classroom.revision ?? 1;
}
onMounted(init);
watch(() => props.classroom.id, init);
async function run(action: () => Promise<void>) {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
function addPeriod(target: Period[]) {
  if (target.length >= 12) return;
  const previous = target.at(-1);
  const start = previous?.end ?? "08:00";
  const minutes = Number(start.slice(0, 2)) * 60 + Number(start.slice(3)) + 40;
  if (minutes >= 24 * 60) {
    error.value = "已接近当日结束，请先调整前一节时间。";
    return;
  }
  target.push({
    start,
    end: `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`,
    subject: props.classroom.subjects[0] ?? "",
  });
}
function loadTemplate(template: any) {
  for (let i = 1; i <= 7; i++)
    weekdays[String(i)] = JSON.parse(
      JSON.stringify(template.weekdays[String(i)] ?? []),
    );
  from.value =
    template.effectiveFrom > dateKey()
      ? template.effectiveFrom
      : addDays(dateKey(), 1);
  until.value =
    template.effectiveUntil >= from.value
      ? template.effectiveUntil
      : addDays(from.value, 120);
  revision.value = props.classroom.revision ?? 1;
  notice.value =
    "已复制模板供修改。已生效模板的调整需选择未来日期，旧区间会保留。";
}
function copyDay() {
  for (let i = 1; i <= 5; i++)
    if (String(i) !== selectedDay.value)
      weekdays[String(i)] = JSON.parse(JSON.stringify(selectedPeriods.value));
  notice.value = "已复制到其他周一至周五，请逐日核对科目后发布。";
}
async function publishTemplate() {
  await run(async () => {
    if (!Object.values(weekdays).some((rows) => rows.length))
      throw new Error("至少配置一天课程；临时停课请使用指定日期调整。");
    const result = await api(
      `/classes/${props.classroom.id}/timetable`,
      "PUT",
      {
        revision: revision.value,
        effectiveFrom: from.value,
        effectiveUntil: until.value,
        weekdays,
      },
    );
    revision.value = result.revision;
    emit("refresh");
    notice.value = "周课表已发布，日期例外仍优先。";
  });
}
async function saveOverride() {
  await run(async () => {
    const result = await api(
      `/classes/${props.classroom.id}/overrides`,
      "PUT",
      { ...override, revision: revision.value },
    );
    revision.value = result.revision;
    emit("refresh");
    notice.value = "指定日期调整已发布，仅影响所选日期。";
  });
}
function editOverride(date: string, value: any) {
  Object.assign(override, JSON.parse(JSON.stringify(value)), { date });
  revision.value = props.classroom.revision ?? 1;
}
async function removeOverride(date: string) {
  await run(async () => {
    const result = await api(
      `/classes/${props.classroom.id}/overrides/${date}`,
      "DELETE",
      { revision: revision.value },
    );
    revision.value = result.revision;
    emit("refresh");
    notice.value = "日期例外已移除，恢复使用当日有效周模板。";
  });
}
async function cancelTemplate(template: any) {
  await run(async () => {
    const result = await api(
      `/classes/${props.classroom.id}/timetable/${template.effectiveFrom}`,
      "DELETE",
      { revision: revision.value },
    );
    revision.value = result.revision;
    emit("refresh");
    notice.value = "尚未生效的模板已取消。";
  });
}
</script>

<template>
  <section class="timetable-management">
    <div class="section-heading">
      <h2><CalendarDays :size="20" />课表与作息</h2>
      <button
        class="text-button"
        @click="
          revision = classroom.revision ?? 1;
          error = '';
          notice = '已读取最新配置修订，请核对当前编辑内容。';
        "
      >
        核对最新配置
      </button>
    </div>
    <div v-if="error" class="notice danger" role="alert">{{ error }}</div>
    <div v-if="notice" class="notice subtle" role="status">{{ notice }}</div>
    <div class="settings-grid">
      <section class="panel span-two">
        <div class="section-heading">
          <h2>已发布课表</h2>
          <label v-if="classroom.templates?.length" class="field"
            >模板区间<select v-model="viewingTemplate">
              <option value="">当前有效模板</option>
              <option
                v-for="t in classroom.templates"
                :key="t.effectiveFrom"
                :value="t.effectiveFrom"
              >
                {{ t.effectiveFrom }} — {{ t.effectiveUntil }}
              </option>
            </select></label
          >
        </div>
        <p class="muted small">
          周模板只读视图。指定日期的临时调整优先于周模板。
        </p>
        <p v-if="!publishedTemplate" class="inline-empty">课表待配置。</p>
        <template v-else
          ><div
            class="weekday-tabs"
            role="tablist"
            aria-label="查看已发布课表星期"
          >
            <button
              v-for="(name, index) in weekdayNames"
              :key="index"
              :class="{ active: viewingDay === String(index + 1) }"
              @click="viewingDay = String(index + 1)"
            >
              {{ name }}
            </button>
          </div>
          <p v-if="!publishedPeriods.length" class="inline-empty">
            该星期没有课程。
          </p>
          <table v-else class="published-timetable">
            <thead>
              <tr>
                <th>节次</th>
                <th>开始</th>
                <th>结束</th>
                <th>科目</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(p, index) in publishedPeriods" :key="index">
                <td>{{ index + 1 }}</td>
                <td>{{ p.start }}</td>
                <td>{{ p.end }}</td>
                <td>{{ p.subject }}</td>
              </tr>
            </tbody>
          </table></template
        >
      </section>
      <section v-if="allowed" class="panel">
        <h2>每周课表</h2>
        <p class="muted small">
          一天最多 12 节。调整已生效模板时选择未来日期，保留过去的课程安排。
        </p>
        <fieldset :disabled="!allowed || busy">
          <div class="form-grid">
            <label class="field"
              >开始生效<input v-model="from" type="date" required /></label
            ><label class="field"
              >结束日期<input v-model="until" type="date" required
            /></label>
          </div>
          <div class="weekday-tabs" role="tablist" aria-label="课表星期">
            <button
              v-for="(label, index) in weekdayNames"
              :key="index"
              type="button"
              role="tab"
              :aria-selected="selectedDay === String(index + 1)"
              :class="{ active: selectedDay === String(index + 1) }"
              @click="selectedDay = String(index + 1)"
            >
              {{ label
              }}<small>{{ weekdays[String(index + 1)]?.length || 0 }}</small>
            </button>
          </div>
          <p v-if="!selectedPeriods.length" class="inline-empty">
            当日未安排课程。
          </p>
          <div
            v-for="(p, index) in selectedPeriods"
            :key="index"
            class="period-row"
          >
            <span>{{ index + 1 }}</span
            ><label
              ><span class="sr-only">第 {{ index + 1 }} 节开始</span
              ><input v-model="p.start" type="time" required /></label
            ><span>至</span
            ><label
              ><span class="sr-only">第 {{ index + 1 }} 节结束</span
              ><input v-model="p.end" type="time" required /></label
            ><label
              ><span class="sr-only">第 {{ index + 1 }} 节科目</span
              ><select v-model="p.subject">
                <option v-for="s in classroom.subjects" :key="s">
                  {{ s }}
                </option>
              </select></label
            ><button
              class="icon-button destructive"
              :aria-label="`移除第 ${index + 1} 节`"
              @click="selectedPeriods.splice(index, 1)"
            >
              <Trash2 :size="16" />
            </button>
          </div>
          <div class="record-actions">
            <button
              class="text-button"
              type="button"
              :disabled="selectedPeriods.length >= 12"
              @click="
                addPeriod(weekdays[selectedDay] ?? (weekdays[selectedDay] = []))
              "
            >
              <Plus :size="16" />增加一节</button
            ><button
              class="text-button"
              :disabled="!selectedPeriods.length"
              @click="copyDay"
            >
              <Copy :size="15" />复制到其他工作日
            </button>
          </div>
          <button class="button primary" @click="publishTemplate">
            发布周课表
          </button>
        </fieldset>
        <div class="template-history">
          <h3>有效模板与历史</h3>
          <p v-if="!classroom.templates?.length" class="inline-empty">
            尚未发布周模板。
          </p>
          <article v-for="t in classroom.templates" :key="t.effectiveFrom">
            <div>
              <strong>{{ t.effectiveFrom }} — {{ t.effectiveUntil }}</strong
              ><small>{{
                t.effectiveUntil < dateKey()
                  ? "已结束"
                  : t.effectiveFrom > dateKey()
                    ? "待生效"
                    : "生效中"
              }}</small>
            </div>
            <button
              class="text-button"
              :disabled="!allowed"
              @click="loadTemplate(t)"
            >
              复制并调整</button
            ><button
              v-if="t.effectiveFrom > dateKey()"
              class="text-button destructive"
              :disabled="!allowed"
              @click="cancelTemplate(t)"
            >
              取消排期
            </button>
          </article>
        </div>
      </section>
      <section class="panel">
        <h2>指定日期调整</h2>
        <p class="muted small">
          用于调休、临时停课或逐节调整，只影响选定日期。
        </p>
        <fieldset :disabled="!allowed || busy">
          <label class="field"
            >日期<input type="date" v-model="override.date" /></label
          ><label class="field"
            >调整方式<select v-model="override.mode">
              <option value="closed">当日停课</option>
              <option value="weekday">采用某星期课程</option>
              <option value="custom">逐节配置当日课程</option>
            </select></label
          ><label v-if="override.mode === 'weekday'" class="field"
            >采用课程<select v-model.number="override.weekday">
              <option
                v-for="(name, index) in weekdayNames"
                :key="index"
                :value="index + 1"
              >
                {{ name }}
              </option>
            </select></label
          ><template v-if="override.mode === 'custom'"
            ><div
              v-for="(p, index) in override.periods"
              :key="index"
              class="override-period"
            >
              <label class="field"
                >开始<input v-model="p.start" type="time" /></label
              ><label class="field"
                >结束<input v-model="p.end" type="time" /></label
              ><label class="field"
                >科目<select v-model="p.subject">
                  <option v-for="s in classroom.subjects" :key="s">
                    {{ s }}
                  </option>
                </select></label
              ><button
                class="icon-button destructive"
                :aria-label="`移除临时课程 ${index + 1}`"
                @click="override.periods.splice(index, 1)"
              >
                <Trash2 :size="16" />
              </button>
            </div>
            <button
              class="text-button"
              :disabled="override.periods.length >= 12"
              @click="addPeriod(override.periods)"
            >
              <Plus :size="16" />增加临时课程
            </button></template
          ><button class="button primary" @click="saveOverride">
            发布日期调整
          </button>
        </fieldset>
        <div class="template-history">
          <h3>已发布日期例外</h3>
          <p
            v-if="!Object.keys(classroom.overrides ?? {}).length"
            class="inline-empty"
          >
            没有临时调整，使用对应的周模板。
          </p>
          <article v-for="(value, date) in classroom.overrides" :key="date">
            <div>
              <strong>{{ date }}</strong
              ><small>{{
                value.mode === "closed"
                  ? "当日停课"
                  : value.mode === "custom"
                    ? `${value.periods.length} 节临时课程`
                    : `采用${weekdayNames[value.weekday - 1]}`
              }}</small>
            </div>
            <button
              class="text-button"
              :disabled="!allowed"
              @click="editOverride(String(date), value)"
            >
              编辑</button
            ><button
              class="text-button destructive"
              :disabled="!allowed"
              @click="removeOverride(String(date))"
            >
              移除调整
            </button>
          </article>
        </div>
      </section>
    </div>
  </section>
</template>
