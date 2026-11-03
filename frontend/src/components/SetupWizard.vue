<script setup lang="ts">
import { reactive, ref } from "vue";
import { ArrowRight, ShieldCheck } from "@lucide/vue";
import { api } from "../api";
const emit = defineEmits<{ initialized: [string] }>();
const form = reactive({ login: "", password: "", setupKey: "" });
const busy = ref(false),
  error = ref("");
async function initialize() {
  busy.value = true;
  error.value = "";
  try {
    const result = await api("/setup/initialize", "POST", {
      ...form,
      name: "系统管理员",
      siteName: "班级看板",
      registrationOpen: false,
    });
    form.password = form.setupKey = "";
    emit("initialized", result.token);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
</script>
<template>
  <main class="auth-card access-card">
    <span class="access-icon"><ShieldCheck :size="24" /></span>
    <p class="eyebrow">仅限学校负责人</p>
    <h2>开通管理员账号</h2>
    <p class="muted">
      使用部署人员提供的管理员激活码。开通后即可为班主任和老师分配账号。
    </p>
    <form @submit.prevent="initialize">
      <label class="field"
        >管理员账号<input
          v-model.trim="form.login"
          required
          minlength="3"
          maxlength="80"
          autocomplete="username"
          placeholder="设置管理员账号"
          pattern="[a-zA-Z0-9_@.\-]+"
      /></label>
      <label class="field"
        >密码<input
          v-model="form.password"
          type="password"
          required
          minlength="10"
          maxlength="128"
          autocomplete="new-password"
          placeholder="设置密码，至少 10 个字符"
      /></label>
      <label class="field"
        >管理员激活码<input
          v-model.trim="form.setupKey"
          type="password"
          required
          autocomplete="off"
          placeholder="由部署人员提供"
      /></label>
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <button class="button primary full" :disabled="busy">
        {{ busy ? "正在开通…" : "开通并进入管理后台" }}<ArrowRight :size="18" />
      </button>
    </form>
    <a class="text-button auth-switch" href="/">我是老师，返回登录</a>
  </main>
</template>
