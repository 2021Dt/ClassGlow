<script setup lang="ts">
import { reactive, ref } from "vue";
import { ArrowRight, School, Check, BookOpen, Bell } from "@lucide/vue";
import { api } from "../api";
defineProps<{
  site: { initialized: boolean; siteName: string };
}>();
const emit = defineEmits<{ authenticated: [string] }>();
const form = reactive({ login: "", password: "" });
const busy = ref(false),
  error = ref("");
async function submit() {
  error.value = "";
  busy.value = true;
  try {
    const result = await api("/auth/login", "POST", form);
    form.password = "";
    emit("authenticated", result.token);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
</script>
<template>
  <div class="auth-shell access-shell">
    <aside class="auth-intro access-intro">
      <div class="brand">
        <span class="brand-mark"><School :size="24" /></span>{{ site.siteName }}
      </div>
      <p class="eyebrow">把时间留给教学</p>
      <h1>今天的安排，<br />一处就好。</h1>
      <p>发作业、看课表、交代班级小事。<br />登录后，进入属于你的工作台。</p>
      <div class="access-scenes" aria-hidden="true">
        <div>
          <span class="access-icon"><BookOpen :size="20" /></span
          ><strong>作业发布</strong><small>各科安排，清楚传达</small
          ><Check :size="16" />
        </div>
        <div>
          <span class="access-icon amber"><Bell :size="20" /></span
          ><strong>班级提醒</strong><small>重要的事，不再遗漏</small
          ><Check :size="16" />
        </div>
      </div>
      <p class="access-caption">手机、电脑都能用，教室看板同步更新。</p>
    </aside>
    <main class="auth-card access-card">
      <h2>登录班级看板</h2>
      <p class="muted">使用管理员给你的账号和密码登录。</p>
      <form @submit.prevent="submit">
        <label class="field"
          >账号<input
            v-model.trim="form.login"
            required
            minlength="3"
            maxlength="80"
            autocomplete="username"
            placeholder="请输入管理员分配的账号"
        /></label>
        <label class="field"
          >密码<input
            v-model="form.password"
            type="password"
            required
            minlength="10"
            maxlength="128"
            autocomplete="current-password"
            placeholder="请输入密码"
        /></label>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
        <button class="button primary full" :disabled="busy">
          {{ busy ? "正在登录…" : "登录" }}<ArrowRight :size="18" />
        </button>
      </form>
      <p class="access-help">没有账号或忘记密码？请联系管理员。</p>
    </main>
  </div>
</template>
