<script setup lang="ts">
import { reactive, ref } from "vue";
import { ArrowRight, Download } from "@lucide/vue";
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
    <main class="auth-card access-card">
      <div class="brand">
        <img
          class="brand-app-icon"
          src="/app-icon.webp"
          width="44"
          height="44"
          alt=""
        />{{ site.siteName }}
      </div>
      <h2>登录</h2>
      <form @submit.prevent="submit">
        <label class="field"
          >账号<input
            v-model.trim="form.login"
            required
            minlength="3"
            maxlength="80"
            autocomplete="username"
            placeholder="请输入账号"
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
      <a class="client-download-entry" href="/download"
        ><Download :size="16" />下载 Windows 客户端</a
      >
    </main>
  </div>
</template>
<style scoped>
.brand-app-icon {
  border-radius: 12px;
}
.client-download-entry {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-top: 22px;
  padding-top: 20px;
  border-top: 1px solid var(--border);
  text-decoration: none;
  font-size: 13px;
}
</style>
