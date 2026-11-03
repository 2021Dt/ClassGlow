<script setup lang="ts">
import { reactive, ref } from "vue";
import { ArrowRight, School, Check, BookOpen, Bell } from "@lucide/vue";
import { api } from "../api";
import SetupWizard from "./SetupWizard.vue";
const props = defineProps<{
  site: { initialized: boolean; siteName: string };
}>();
const emit = defineEmits<{ authenticated: [string] }>();
const adminEntry = location.pathname === "/setup";
const mode = ref<"login" | "activate">("login");
const form = reactive({ login: "", password: "", activationCode: "" });
const busy = ref(false),
  error = ref("");
function switchMode(value: "login" | "activate") {
  mode.value = value;
  error.value = "";
  form.password = "";
}
async function submit() {
  error.value = "";
  if (!props.site.initialized) {
    error.value = "学校尚未开通，请联系管理员。老师无需配置服务器。";
    return;
  }
  busy.value = true;
  try {
    const body =
      mode.value === "activate"
        ? form
        : { login: form.login, password: form.password };
    const result = await api(`/auth/${mode.value}`, "POST", body);
    form.password = form.activationCode = "";
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
    <SetupWizard
      v-if="adminEntry && !site.initialized"
      @initialized="emit('authenticated', $event)"
    />
    <main v-else class="auth-card access-card">
      <div class="access-tabs" role="tablist" aria-label="账号入口">
        <button
          role="tab"
          :aria-selected="mode === 'login'"
          :class="{ active: mode === 'login' }"
          @click="switchMode('login')"
        >
          账号登录
        </button>
        <button
          role="tab"
          :aria-selected="mode === 'activate'"
          :class="{ active: mode === 'activate' }"
          @click="switchMode('activate')"
        >
          首次激活
        </button>
      </div>
      <h2>{{ mode === "login" ? "欢迎回来" : "激活你的账号" }}</h2>
      <p class="muted">
        {{
          mode === "login"
            ? "输入账号和密码，开始今天的工作。"
            : "管理员已为你安排好身份和班级，设置密码即可开始。"
        }}
      </p>
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
          >{{ mode === "activate" ? "设置密码" : "密码"
          }}<input
            v-model="form.password"
            type="password"
            required
            minlength="10"
            maxlength="128"
            :autocomplete="
              mode === 'activate' ? 'new-password' : 'current-password'
            "
            :placeholder="
              mode === 'activate' ? '至少 10 个字符' : '请输入密码'
            "
        /></label>
        <label v-if="mode === 'activate'" class="field"
          >激活码<input
            v-model.trim="form.activationCode"
            required
            maxlength="40"
            autocomplete="off"
            placeholder="请输入管理员提供的激活码"
        /></label>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
        <p v-if="!site.initialized" class="access-pending">
          学校正在开通中。老师请等待管理员发放账号和激活码，无需进行系统设置。
        </p>
        <button class="button primary full" :disabled="busy">
          {{
            busy
              ? "正在处理…"
              : mode === "login"
                ? "登录工作台"
                : "激活并进入工作台"
          }}<ArrowRight :size="18" />
        </button>
      </form>
      <p class="access-help">
        {{
          mode === "login"
            ? "忘记密码？请联系学校管理员重置。"
            : "激活码仅需使用一次。已激活的账号请直接登录。"
        }}
      </p>
      <a v-if="!site.initialized" class="text-button auth-switch" href="/setup"
        >管理员首次开通</a
      >
    </main>
  </div>
</template>
