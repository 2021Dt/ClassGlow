<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { Check, Copy, Plus, ShieldCheck, Users, X } from "@lucide/vue";
import { api } from "../api";
import SubjectPicker from "./SubjectPicker.vue";
import { isAdmin, roleLabel, timeLabel, type Classroom } from "../domain";

const props = defineProps<{ classroom: Classroom }>();
const emit = defineEmits<{ refresh: [] }>();
const editable = computed(
  () => isAdmin(props.classroom) && !props.classroom.archived,
);
const error = ref(""),
  success = ref(""),
  busy = ref(false),
  members = ref<any[]>([]),
  invitations = ref<any[]>([]),
  outgoing = ref<any>(null);
const config = reactive({
  name: "",
  stage: "小学",
  subjects: [] as string[],
  revision: 1,
});
const inviteSubjects = ref<string[]>([]),
  inviteUrl = ref(""),
  recipient = ref("");
const editing = ref<any>(null),
  memberDialog = ref<HTMLDialogElement>(),
  confirmation = ref<HTMLDialogElement>();
const confirmText = ref("");
let operation: (() => Promise<void>) | undefined;
function syncConfig() {
  config.name = props.classroom.name;
  config.stage = props.classroom.stage;
  config.subjects = [...props.classroom.subjects];
  config.revision = props.classroom.revision ?? 1;
  inviteSubjects.value = props.classroom.subjects.slice(0, 1);
}
async function run(action: () => Promise<void>) {
  error.value = "";
  success.value = "";
  busy.value = true;
  try {
    await action();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function load() {
  const result = await api(`/classes/${props.classroom.id}/members`);
  members.value = result.members;
  invitations.value = result.applications;
  outgoing.value =
    props.classroom.role === "owner"
      ? await api(`/classes/${props.classroom.id}/transfer`)
      : null;
}
watch(
  () => props.classroom.id,
  () => {
    syncConfig();
    run(load);
  },
);
onMounted(() => {
  syncConfig();
  run(load);
});
async function saveConfig() {
  await run(async () => {
    const result = await api(`/classes/${props.classroom.id}/settings`, "PUT", {
      ...config,
      subjects: config.subjects,
    });
    config.revision = result.revision;
    emit("refresh");
    success.value = "班级资料与学科已更新。移除学科会同时撤销对应发布授权。";
  });
}
async function createInvitation() {
  await run(async () => {
    const result = await api(`/classes/${props.classroom.id}/invites`, "POST", {
      subjects: inviteSubjects.value,
    });
    inviteUrl.value = result.url;
    await load();
  });
}
async function copyInvite() {
  await run(async () => {
    await navigator.clipboard.writeText(inviteUrl.value);
    success.value = "邀请链接已复制。";
  });
}
function editMember(m: any) {
  editing.value = JSON.parse(JSON.stringify(m));
  memberDialog.value?.showModal();
}
async function saveMember(active = true) {
  await run(async () => {
    await api(
      `/classes/${props.classroom.id}/members/${editing.value.id}`,
      "PUT",
      { role: editing.value.role, subjects: editing.value.subjects, active },
    );
    memberDialog.value?.close();
    await load();
    emit("refresh");
    success.value = active
      ? "成员权限已更新，后续请求立即按新权限校验。"
      : "成员已移除，历史内容保留。";
  });
}
async function decide(i: any, action: string) {
  await run(async () => {
    await api(
      `/classes/${props.classroom.id}/invites/${i.id}/${action}`,
      "POST",
    );
    await load();
    success.value =
      action === "approve"
        ? "已批准入班并合并学科授权。"
        : "申请或邀请已关闭。";
  });
}
function ask(text: string, action: () => Promise<void>) {
  confirmText.value = text;
  operation = action;
  confirmation.value?.showModal();
}
async function confirm() {
  await run(async () => {
    await operation?.();
    confirmation.value?.close();
  });
}
async function transfer() {
  const target = members.value.find((m) => m.id === recipient.value);
  if (!target) return;
  ask(
    `将${props.classroom.name}的所有权转给${target.name}。对方接受前你的权限不变，请求 24 小时有效。`,
    async () => {
      await api(`/classes/${props.classroom.id}/transfers`, "POST", {
        recipient: target.id,
      });
      await load();
      success.value = "交接请求已创建，请对方登录后在“我的账号”中接受。";
    },
  );
}
async function cancelTransfer() {
  await run(async () => {
    await api(
      `/classes/${props.classroom.id}/transfers/${outgoing.value.id}/cancel`,
      "POST",
    );
    await load();
    success.value = "交接请求已取消。";
  });
}
</script>

<template>
  <section class="class-maintenance settings-grid">
    <div v-if="error" class="notice danger span-two" role="alert">
      {{ error }}
    </div>
    <div v-if="success" class="notice subtle span-two" role="status">
      <Check :size="17" />{{ success }}
    </div>
    <section class="panel">
      <h2>班级资料与学科</h2>
      <form @submit.prevent="saveConfig">
        <fieldset :disabled="!editable || busy">
          <label class="field"
            >班级名称<input
              v-model="config.name"
              required
              maxlength="60" /></label
          ><label class="field"
            >学段<AppSelect v-model="config.stage">
              <option>小学</option>
              <option>初中</option>
              <option>高中</option>
            </AppSelect></label
          >
          <SubjectPicker v-model="config.subjects" :stage="config.stage" />
          <p class="muted small">
            移除学科会撤销成员的对应发布授权，历史内容保留。
          </p>
          <button class="button primary" type="submit">保存班级设置</button
          ><button class="text-button" type="button" @click="syncConfig">
            读取最新设置
          </button>
        </fieldset>
      </form>
    </section>
    <section class="panel">
      <div class="section-heading">
        <h2><Users :size="19" />成员与权限</h2>
        <button class="text-button" @click="run(load)">刷新</button>
      </div>
      <article class="member-row" v-for="m in members" :key="m.id">
        <span class="avatar">{{ m.name.slice(0, 1) }}</span>
        <div>
          <strong>{{ m.name }}</strong
          ><small
            >{{ roleLabel(m.role) }} · {{ m.login }} ·
            {{ m.subjects.join("、") || "未授权学科" }}</small
          >
        </div>
        <button
          v-if="
            editable &&
            m.role !== 'owner' &&
            (classroom.role === 'owner' || m.role === 'teacher')
          "
          class="text-button"
          @click="editMember(m)"
        >
          修改权限
        </button>
      </article>
      <div v-if="editable" class="invite-form">
        <strong>邀请科任老师</strong>
        <div class="subject-checks">
          <label v-for="subject in classroom.subjects" :key="subject"
            ><input
              type="checkbox"
              v-model="inviteSubjects"
              :value="subject"
            />{{ subject }}</label
          >
        </div>
        <button
          class="button secondary"
          :disabled="busy || !inviteSubjects.length"
          @click="createInvitation"
        >
          <Plus :size="16" />生成单人邀请
        </button>
        <div v-if="inviteUrl" class="invite-result">
          <input readonly :value="inviteUrl" aria-label="单人邀请链接" /><button
            class="button secondary"
            @click="copyInvite"
          >
            <Copy :size="16" />复制
          </button>
        </div>
        <small
          >72
          小时有效，老师登录申请后仍需管理员审批。邀请和设备绑定码用途不同。</small
        >
      </div>
      <article v-for="i in invitations" :key="i.id" class="application-row">
        <p>
          {{ i.applicant || "等待老师申请" }} {{ i.applicantLogin }} ·
          {{ i.subjects.join("、") }} ·
          {{
            { open: "邀请有效", pending: "等待审批", expired: "已过期" }[
              i.state as string
            ] || i.state
          }}
        </p>
        <template v-if="editable"
          ><button
            v-if="i.state === 'pending'"
            class="text-button"
            @click="decide(i, 'approve')"
          >
            批准</button
          ><button
            v-if="i.state === 'pending'"
            class="text-button destructive"
            @click="decide(i, 'reject')"
          >
            拒绝</button
          ><button
            v-if="i.state === 'open' || i.state === 'pending'"
            class="text-button destructive"
            @click="decide(i, 'revoke')"
          >
            撤销邀请
          </button></template
        >
      </article>
    </section>
    <section v-if="classroom.role === 'owner'" class="panel span-two">
      <h2><ShieldCheck :size="19" />班级所有权交接</h2>
      <p class="muted small">
        每班始终只有一个所有者。对方接受后，你保留管理员身份；接受前可以取消。
      </p>
      <div v-if="outgoing?.state === 'pending'" class="notice subtle">
        等待 {{ members.find((m) => m.id === outgoing.recipient)?.name }} 接受 ·
        截止 {{ timeLabel(outgoing.expiresAt * 1000, true)
        }}<button
          class="text-button"
          :disabled="!editable"
          @click="cancelTransfer"
        >
          取消请求
        </button>
      </div>
      <div v-else class="transfer-form">
        <label class="field"
          >接收成员<AppSelect v-model="recipient" :disabled="!editable">
            <option value="">请选择本班有效成员</option>
            <option
              v-for="m in members.filter((m) => m.role !== 'owner')"
              :key="m.id"
              :value="m.id"
            >
              {{ m.name }} · {{ roleLabel(m.role) }}
            </option>
          </AppSelect></label
        ><button
          class="button secondary"
          :disabled="!editable || !recipient"
          @click="transfer"
        >
          发起所有权交接
        </button>
      </div>
    </section>
  </section>
  <dialog ref="memberDialog" class="modal">
    <header class="section-heading">
      <h2>{{ editing?.name }} · 成员权限</h2>
      <button
        class="icon-button"
        aria-label="关闭成员权限"
        @click="memberDialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <template v-if="editing"
      ><label class="field"
        >角色<AppSelect
          v-model="editing.role"
          :disabled="classroom.role !== 'owner'"
        >
          <option value="teacher">科任老师</option>
          <option value="admin">管理员</option>
        </AppSelect></label
      >
      <div class="subject-checks">
        <label v-for="s in classroom.subjects" :key="s"
          ><input type="checkbox" :value="s" v-model="editing.subjects" />{{
            s
          }}</label
        >
      </div>
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <div class="modal-actions">
        <button
          class="text-button destructive"
          @click="
            ask(
              `移除${editing.name}后，已发布内容保留，未提交草稿不能再向本班发布。`,
              async () => {
                await saveMember(false);
              },
            )
          "
        >
          移除成员</button
        ><button class="button primary" :disabled="busy" @click="saveMember()">
          保存权限
        </button>
      </div></template
    >
  </dialog>
  <dialog ref="confirmation" class="modal">
    <h2>确认班级操作</h2>
    <p>{{ confirmText }}</p>
    <p v-if="error" class="field-error">{{ error }}</p>
    <div class="modal-actions">
      <button class="button secondary" @click="confirmation?.close()">
        取消</button
      ><button class="button primary" :disabled="busy" @click="confirm">
        确认
      </button>
    </div>
  </dialog>
</template>
