<script setup lang="ts">
import DatePicker from "./DatePicker.vue";
import { computed, onMounted, reactive, ref, watch } from "vue";
import { Download, X } from "@lucide/vue";
import ClassMaintenance from "./ClassMaintenance.vue";
import TimetableEditor from "./TimetableEditor.vue";
import { api } from "../api";
import { isAdmin, timeLabel, type Classroom } from "../domain";

const props = defineProps<{ classroom: Classroom }>();
const emit = defineEmits<{ refresh: [] }>();
const allowed = computed(
  () => isAdmin(props.classroom) && !props.classroom.archived,
);
const form = reactive({ message: "", event: "", eventDate: "", revision: 1 });
const tab = ref("timetable");
const error = ref(""),
  notice = ref(""),
  confirmation = ref<HTMLDialogElement>(),
  busy = ref(false);
const audits = ref<any[]>([]);
const auditLabels: Record<string, string> = {
  class_created: "创建班级",
  layout_published: "发布布局",
  display_published: "发布寄语与倒计时",
  timetable_published: "发布周课表",
  override_published: "发布日期调整",
  override_removed: "移除日期调整",
  class_settings_updated: "修改班级设置",
  membership_changed: "调整成员权限",
  invitation_created: "创建邀请",
  invitation_approved: "批准入班",
  invitation_rejected: "拒绝入班",
  invitation_revoked: "撤销邀请",
  device_bound: "绑定设备",
  device_revoked: "撤销设备",
  device_uninstalled: "教室端卸载并撤销授权",
  device_command: "提交设备控制",
  transfer_accepted: "完成所有权交接",
  transfer_cancelled: "取消所有权交接",
  archive: "归档班级",
  restore: "恢复班级",
  future_template_cancelled: "取消课表排期",
};
async function loadAudits() {
  await run(async () => {
    audits.value = await api(`/classes/${props.classroom.id}/audit`);
  });
}
let baseline = "";
const payload = () =>
  JSON.stringify([form.message, form.event, form.eventDate]);
function load() {
  Object.assign(form, {
    message: props.classroom.message,
    event: props.classroom.event,
    eventDate: props.classroom.eventDate,
    revision: props.classroom.revision ?? 1,
  });
  baseline = payload();
}
onMounted(load);
watch(() => props.classroom.id, load);
watch(
  () => props.classroom.revision,
  () => {
    if (payload() === baseline) load();
  },
);
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
async function saveDisplay() {
  await run(async () => {
    if (!!form.event !== !!form.eventDate)
      throw new Error("倒计时请同时填写事件名称和目标日期，或同时清空。");
    const value = await api(
      `/classes/${props.classroom.id}/display`,
      "PUT",
      form,
    );
    form.revision = value.revision;
    baseline = payload();
    emit("refresh");
    notice.value = "寄语与倒计时已发布。";
  });
}
async function exportData() {
  await run(async () => {
    const value = await api(`/classes/${props.classroom.id}/export`);
    const url = URL.createObjectURL(
      new Blob([JSON.stringify(value, null, 2)], { type: "application/json" }),
    );
    const link = document.createElement("a");
    link.href = url;
    link.download = `${props.classroom.name}-班级配置.json`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
}
async function archive() {
  await run(async () => {
    await api(
      `/classes/${props.classroom.id}/${props.classroom.archived ? "restore" : "archive"}`,
      "POST",
    );
    confirmation.value?.close();
    emit("refresh");
    notice.value = props.classroom.archived
      ? "班级恢复后需重新绑定设备。"
      : "班级已归档，设备凭证永久失效。";
  });
}
</script>

<template>
  <div class="settings-page">
    <nav class="settings-tabs" aria-label="班级设置分类">
      <button
        v-for="item in [
          { id: 'timetable', label: '课表' },
          { id: 'display', label: '寄语与倒计时' },
          { id: 'members', label: '班级成员' },
          { id: 'data', label: '数据与记录' },
        ]"
        :key="item.id"
        :class="{ active: tab === item.id }"
        @click="tab = item.id"
      >
        {{ item.label }}
      </button>
    </nav>
    <div v-if="error" class="notice danger" role="alert">{{ error }}</div>
    <div v-if="notice" class="notice subtle" role="status">{{ notice }}</div>
    <div
      v-if="tab === 'display' || tab === 'data'"
      class="settings-grid settings-single-panel"
    >
      <section v-if="tab === 'display'" class="panel">
        <h2>寄语与倒计时</h2>
        <form @submit.prevent="saveDisplay">
          <fieldset :disabled="!allowed || busy">
            <label class="field"
              >班主任寄语<textarea
                v-model="form.message"
                rows="3"
                maxlength="80"
                placeholder="给同学们的一句话，最多 80 字"
              />
            </label>
            <div class="form-grid">
              <label class="field"
                >倒计时事件<input
                  v-model="form.event"
                  maxlength="40"
                  placeholder="例如期末考试" /></label
              ><label class="field"
                >目标日期<DatePicker v-model="form.eventDate" type="date"
              /></label>
            </div>
            <button class="button primary">发布展示信息</button
            ><button type="button" class="text-button" @click="load">
              撤销未保存修改
            </button>
          </fieldset>
        </form>
      </section>
      <section v-if="tab === 'data'" class="panel">
        <h2>数据与班级维护</h2>
        <p class="muted small">
          导出包含本班配置、正式内容和历史，不包含账号口令、会话、设备凭证或私人草稿。
        </p>
        <button
          class="button secondary"
          :disabled="!isAdmin(classroom) || busy"
          @click="exportData"
        >
          <Download :size="17" />导出班级配置与历史
        </button>
        <div class="form-divider"></div>
        <p class="muted small">
          归档使班级只读，撤销设备授权和未完成的邀请、交接请求。恢复会保留历史，需要重新绑定设备。
        </p>
        <button
          v-if="classroom.role === 'owner'"
          class="button secondary destructive"
          :disabled="busy"
          @click="confirmation?.showModal()"
        >
          {{ classroom.archived ? "恢复班级" : "归档班级" }}
        </button>
      </section>
    </div>
    <ClassMaintenance
      v-if="tab === 'members'"
      :classroom="classroom"
      @refresh="emit('refresh')"
    /><TimetableEditor
      v-if="tab === 'timetable'"
      :classroom="classroom"
      @refresh="emit('refresh')"
    />
    <section
      v-if="tab === 'data' && isAdmin(classroom)"
      class="panel audit-panel"
    >
      <div class="section-heading">
        <h2>班级维护记录</h2>
        <button class="text-button" @click="loadAudits">读取最近操作</button>
      </div>
      <p v-if="!audits.length" class="muted small">
        查看成员、配置、设备与归档等维护操作。详细变更包含在班级导出中。
      </p>
      <article v-for="entry in audits" :key="entry.id" class="audit-row">
        <strong>{{ auditLabels[entry.action] || "班级维护操作" }}</strong
        ><span>{{ entry.actor }}</span
        ><time>{{ timeLabel(entry.at * 1000, true) }}</time>
      </article>
    </section>
  </div>
  <dialog ref="confirmation" class="modal">
    <header class="section-heading">
      <h2>{{ classroom.archived ? "恢复班级" : "归档班级" }}</h2>
      <button
        class="icon-button"
        aria-label="关闭归档确认"
        @click="confirmation?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <p>
      {{ classroom.name }}。{{
        classroom.archived
          ? "历史记录将继续保留，旧凭证不会恢复，需要重新绑定设备。"
          : "操作后班级只读，现有设备凭证与未完成邀请永久失效。持续离线设备需恢复联网后才能停止使用。"
      }}
    </p>
    <p v-if="error" class="field-error">{{ error }}</p>
    <div class="modal-actions">
      <button class="button secondary" @click="confirmation?.close()">
        取消</button
      ><button class="button primary" :disabled="busy" @click="archive">
        确认{{ classroom.archived ? "恢复" : "归档" }}
      </button>
    </div>
  </dialog>
</template>

<style scoped>
.settings-tabs {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 22px;
}
.settings-tabs button {
  padding: 10px 18px;
  border-radius: 10px;
  background: transparent;
  color: var(--muted);
  font-size: 13px;
}
.settings-tabs button.active {
  color: var(--green);
  background: #e5efe8;
  font-weight: 600;
}
.settings-grid {
  grid-template-columns: minmax(0, 1fr);
}
.settings-grid > .panel {
  max-width: 900px;
}
</style>
