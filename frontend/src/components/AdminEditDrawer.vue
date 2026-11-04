<script setup lang="ts">
import DatePicker from "./DatePicker.vue";
import { computed, reactive, ref } from "vue";
import { X, Search, ArrowUpRight, Database, Monitor } from "@lucide/vue";
import { api, session } from "../api";
import { accountRole, adminDate } from "../admin";
const emit = defineEmits<{
  changed: [string];
  restored: [string];
  openClass: [string];
}>();
const dialog = ref<HTMLDialogElement>(),
  mode = ref(""),
  target = ref<any>(null),
  busy = ref(false),
  error = ref("");
const userForm = reactive({
  name: "",
  identity: "teacher",
  active: true,
  expiry: "",
});
const classForm = reactive({ name: "", ownerId: "", archived: false }),
  ownerQuery = ref(""),
  owners = ref<any[]>([]);
const restoreForm = reactive({ password: "", confirmation: "" });
const securityForm = reactive({
  minutes: 30,
  reason: "异常登录，需要确认账号安全",
});
const recoveryResult = ref<{ code: string; expiresAt: number }>();
const isFrozen = computed(
  () => target.value?.frozenUntil > (Date.now() + session.clockOffset) / 1000,
);
const titles: Record<string, string> = {
  edit: "编辑账号",
  password: "重置密码",
  security: "账号安全",
  class: "管理班级",
  revoke: "解绑教室设备",
  restore: "恢复数据备份",
};
function open(kind: string, item: any) {
  mode.value = kind;
  target.value = item;
  error.value = "";
  recoveryResult.value = undefined;
  Object.assign(securityForm, {
    minutes: 30,
    reason: "异常登录，需要确认账号安全",
  });
  Object.assign(userForm, {
    name: item.name ?? "",
    identity: item.systemAdmin
      ? "system_admin"
      : item.canCreateClasses
        ? "headteacher"
        : "teacher",
    active: item.active ?? true,
    expiry: item.expiresAt
      ? new Date(item.expiresAt * 1000 + 8 * 3600000).toISOString().slice(0, 10)
      : "",
  });
  if (kind === "class") {
    Object.assign(classForm, {
      name: item.name,
      ownerId: item.owner.id,
      archived: item.archived,
    });
    owners.value = [item.owner];
    ownerQuery.value = "";
  }
  restoreForm.password = restoreForm.confirmation = "";
  dialog.value?.showModal();
}
function preset(days: number) {
  userForm.expiry = days
    ? new Date(Date.now() + days * 86400000 + 8 * 3600000)
        .toISOString()
        .slice(0, 10)
    : "";
}
async function findOwners() {
  busy.value = true;
  error.value = "";
  try {
    owners.value = (
      await api(
        `/system/users?status=active&limit=100&search=${encodeURIComponent(ownerQuery.value)}`,
      )
    ).items;
    if (!owners.value.some((u) => u.id === target.value.owner.id))
      owners.value.unshift(target.value.owner);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function save() {
  busy.value = true;
  error.value = "";
  try {
    if (mode.value === "edit")
      await api(`/system/users/${target.value.id}`, "PUT", {
        name: userForm.name,
        active: userForm.active,
        systemAdmin: userForm.identity === "system_admin",
        canCreateClasses: userForm.identity !== "teacher",
        expiresAt: userForm.expiry
          ? new Date(`${userForm.expiry}T23:59:59+08:00`).getTime() / 1000
          : 0,
      });
    else if (mode.value === "password") {
      if (recoveryResult.value) {
        dialog.value?.close();
        return;
      }
      recoveryResult.value = await api(
        `/system/users/${target.value.id}/recovery-code`,
        "POST",
      );
      emit("changed", "重置码已生成，请单独交给账号本人。");
      return;
    } else if (mode.value === "security") {
      if (isFrozen.value)
        await api(`/system/users/${target.value.id}/unfreeze`, "POST");
      else
        await api(
          `/system/users/${target.value.id}/freeze`,
          "POST",
          securityForm,
        );
    } else if (mode.value === "class")
      await api(`/system/classes/${target.value.id}`, "PUT", classForm);
    else if (mode.value === "revoke")
      await api(`/system/devices/${target.value.id}/revoke`, "POST");
    else if (mode.value === "restore") {
      const r = await api(
        `/system/backups/${encodeURIComponent(target.value.name)}/restore`,
        "POST",
        restoreForm,
      );
      dialog.value?.close();
      emit("restored", r.message);
      return;
    }
    dialog.value?.close();
    emit(
      "changed",
      mode.value === "security"
        ? isFrozen.value
          ? "安全冻结已解除，老师需重新登录。"
          : "账号已临时冻结。"
        : "修改已保存。",
    );
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
defineExpose({ open });
</script>
<template>
  <dialog
    ref="dialog"
    class="admin-drawer"
    @cancel="busy && $event.preventDefault()"
  >
    <header>
      <div>
        <span class="admin-mini-label">管理操作</span>
        <h2>{{ titles[mode] }}</h2>
      </div>
      <button
        class="icon-button"
        aria-label="关闭编辑面板"
        :disabled="busy"
        @click="dialog?.close()"
      >
        <X :size="20" />
      </button>
    </header>
    <form @submit.prevent="save">
      <div class="admin-drawer-body">
        <div
          v-if="['edit', 'password', 'security'].includes(mode)"
          class="drawer-person"
        >
          <span class="admin-avatar">{{ target?.name.slice(0, 1) }}</span>
          <div>
            <strong>{{ target?.name }}</strong
            ><small>{{ target?.login }}</small>
          </div>
          <span class="admin-pill muted">{{
            target ? accountRole(target) : ""
          }}</span>
        </div>
        <fieldset :disabled="busy">
          <template v-if="mode === 'edit'"
            ><h3 class="admin-form-section">基本信息</h3>
            <label class="field"
              >姓名<input
                v-model.trim="userForm.name"
                required
                maxlength="40" /></label
            ><label class="field"
              >账号身份<AppSelect
                v-model="userForm.identity"
                :disabled="target?.id === session.userId"
              >
                <option value="teacher">普通老师</option>
                <option value="headteacher">班主任</option>
                <option value="system_admin">系统管理员</option>
              </AppSelect></label
            >
            <p class="admin-field-help">
              身份控制全站功能入口；班级内的任教与管理权限在班级成员中调整。
            </p>
            <h3 class="admin-form-section">账号期限与状态</h3>
            <label class="field"
              >账号到期日<DatePicker
                type="date"
                v-model="userForm.expiry"
                :disabled="target?.id === session.userId"
            /></label>
            <div v-if="target?.id !== session.userId" class="admin-presets">
              <button type="button" @click="preset(0)">长期有效</button
              ><button type="button" @click="preset(30)">30 天后</button
              ><button type="button" @click="preset(365)">一年后</button>
            </div>
            <label class="admin-toggle"
              ><span
                ><strong>允许登录</strong
                ><small>关闭后退出全部登录，保留教学数据。</small></span
              ><input
                type="checkbox"
                v-model="userForm.active"
                :disabled="target?.id === session.userId"
            /></label>
            <div v-if="target?.classes?.length" class="drawer-assignments">
              <h3 class="admin-form-section">当前任教班级</h3>
              <button
                v-for="c in target.classes"
                :key="c.id"
                type="button"
                @click="
                  dialog?.close();
                  emit('openClass', c.id);
                "
              >
                <span
                  >{{ c.name
                  }}<small>{{
                    c.subjects.join("、") || "班级管理"
                  }}</small></span
                ><ArrowUpRight :size="15" />
              </button></div
          ></template>
          <template v-else-if="mode === 'password'"
            ><p class="admin-tip-text">
              核实本人身份后生成一次性重置码，由老师在登录页设置新密码。
            </p>
            <template v-if="recoveryResult">
              <label class="field"
                >一次性重置码<input
                  class="recovery-code"
                  readonly
                  :value="recoveryResult.code"
              /></label>
              <p class="admin-field-help">
                {{ adminDate(recoveryResult.expiresAt, true) }}
                前有效，仅可使用一次。
              </p>
            </template>
            <p class="admin-field-help">
              老师完成改密后，旧登录全部退出，安全冻结解除。生成重置码本身不会解冻账号。
            </p></template
          >
          <template v-else-if="mode === 'security'">
            <template v-if="isFrozen">
              <h3 class="admin-form-section">安全冻结中</h3>
              <p class="admin-tip-text">{{ target.freezeReason }}</p>
              <p class="admin-field-help">
                {{ adminDate(target.frozenUntil, true) }} 后可重新登录。
              </p>
              <p class="admin-tip-text">
                确认本人操作后可提前解除冻结，旧登录不会恢复。
              </p>
              <button
                type="button"
                class="button secondary"
                @click="mode = 'password'"
              >
                生成重置码并协助改密
              </button>
            </template>
            <template v-else>
              <label class="field"
                >冻结时间<AppSelect v-model.number="securityForm.minutes">
                  <option :value="30">30 分钟</option>
                  <option :value="120">2 小时</option>
                  <option :value="1440">24 小时</option>
                </AppSelect></label
              >
              <label class="field"
                >冻结原因<textarea
                  v-model.trim="securityForm.reason"
                  required
                  minlength="2"
                  maxlength="200"
                  rows="3"
                />
              </label>
              <p class="admin-field-help">
                暂时停止该账号登录、发布和控制；保留班级数据与教室当前展示，到期后可重新登录。
              </p>
            </template>
          </template>
          <template v-else-if="mode === 'class'"
            ><label class="field"
              >班级名称<input
                v-model.trim="classForm.name"
                required
                maxlength="60"
            /></label>
            <h3 class="admin-form-section">班级负责人</h3>
            <div class="admin-search">
              <Search :size="17" /><input
                v-model="ownerQuery"
                placeholder="搜索负责人姓名或账号"
                aria-label="搜索班级负责人"
              /><button type="button" @click="findOwners">查找</button>
            </div>
            <label class="field"
              >选择负责人<AppSelect v-model="classForm.ownerId" required>
                <option
                  v-for="owner in owners"
                  :key="owner.id"
                  :value="owner.id"
                >
                  {{ owner.name }} · {{ owner.login }}
                </option>
              </AppSelect></label
            >
            <p class="admin-field-help">
              更换负责人后，原负责人保留班级管理员身份。
            </p>
            <label class="admin-toggle"
              ><span
                ><strong>归档班级</strong
                ><small>归档后保留历史内容，并解除设备绑定。</small></span
              ><input type="checkbox" v-model="classForm.archived" /></label
          ></template>
          <template v-else-if="mode === 'revoke'"
            ><div class="admin-danger-box">
              <Monitor :size="24" />
              <h3>解绑 {{ target?.name }}</h3>
              <p>
                该电脑将停止获取班级内容。再次使用时，需要在教室客户端重新配对。
              </p>
            </div></template
          >
          <template v-else-if="mode === 'restore'"
            ><div class="admin-danger-box">
              <Database :size="24" />
              <h3>用备份替换全站数据</h3>
              <p>{{ target?.name }}</p>
              <p>所有账号与教室设备需重新登录、绑定。系统会先备份当前数据。</p>
            </div>
            <label class="field"
              >当前管理员密码<input
                type="password"
                v-model="restoreForm.password"
                autocomplete="current-password"
                required /></label
            ><label class="field"
              >输入“恢复备份”确认<input
                v-model="restoreForm.confirmation"
                required
                pattern="恢复备份"
                autocomplete="off" /></label
          ></template>
        </fieldset>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      </div>
      <footer class="admin-drawer-footer">
        <button
          type="button"
          class="button secondary"
          :disabled="busy"
          @click="dialog?.close()"
        >
          取消</button
        ><button
          class="button"
          :class="
            ['restore', 'revoke'].includes(mode) ? 'danger-button' : 'primary'
          "
          :disabled="
            busy || (mode === 'security' && target?.id === session.userId)
          "
        >
          {{
            busy
              ? "正在保存…"
              : mode === "restore"
                ? "确认恢复"
                : mode === "revoke"
                  ? "确认解绑"
                  : mode === "password"
                    ? recoveryResult
                      ? "完成"
                      : "生成重置码"
                    : mode === "security"
                      ? isFrozen
                        ? "解除冻结"
                        : "确认临时冻结"
                      : "保存修改"
          }}
        </button>
      </footer>
    </form>
  </dialog>
</template>
