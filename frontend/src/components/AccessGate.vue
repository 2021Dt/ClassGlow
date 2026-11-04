<script setup lang="ts">
import { computed, onUnmounted, reactive, ref } from "vue";
import { ArrowRight, Download, ShieldCheck } from "@lucide/vue";
import { api, ApiError, authNotice, browserIdentity, session } from "../api";
defineProps<{
  site: { initialized: boolean; siteName: string };
}>();
const emit = defineEmits<{ authenticated: [string] }>();
const form = reactive({ login: session.login, password: "" });
const recovery = reactive({ code: "", password: "", confirm: "" });
const mode = ref(
  authNotice.value?.code === "account_frozen" ? "frozen" : "login",
);
const success = ref("");
const notice = ref(authNotice.value);
const clockOffset = ref(
  (notice.value?.serverTime ?? Date.now() / 1000) * 1000 - Date.now(),
);
const clock = ref(Date.now());
const timer = setInterval(() => {
  clock.value = Date.now();
}, 10000);
onUnmounted(() => clearInterval(timer));
const releaseTime = computed(() =>
  new Intl.DateTimeFormat("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(new Date((notice.value?.frozenUntil ?? 0) * 1000)),
);
const freezeEnded = computed(
  () =>
    clock.value + clockOffset.value >= (notice.value?.frozenUntil ?? 0) * 1000,
);
const busy = ref(false),
  error = ref("");
function changeMode(value: string) {
  mode.value = value;
  error.value = "";
  form.password = recovery.password = recovery.confirm = "";
}
async function submit() {
  error.value = success.value = "";
  busy.value = true;
  try {
    const result = await api("/auth/login", "POST", {
      ...form,
      clientId: browserIdentity(),
    });
    form.password = "";
    authNotice.value = null;
    emit("authenticated", result.token);
  } catch (e) {
    if (e instanceof ApiError && e.code === "account_frozen") {
      notice.value = { message: e.message, code: e.code, ...e.details };
      clockOffset.value =
        (e.details?.serverTime ?? Date.now() / 1000) * 1000 - Date.now();
      changeMode("frozen");
    } else error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function recover() {
  error.value = "";
  if (recovery.password !== recovery.confirm) {
    error.value = "两次输入的密码不一致。";
    return;
  }
  busy.value = true;
  try {
    const result = await api("/auth/recover-password", "POST", {
      login: form.login,
      password: recovery.password,
      code: recovery.code,
    });
    recovery.code = "";
    authNotice.value = notice.value = null;
    changeMode("login");
    success.value = result.message;
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
      <section v-if="mode === 'frozen'" class="freeze-state" aria-live="polite">
        <div class="security-symbol"><ShieldCheck :size="30" /></div>
        <h2>账号暂时冻结</h2>
        <p>为保护你的账号安全，账号已暂时冻结。</p>
        <div class="release-time">
          <span>可重新登录时间</span><strong>{{ releaseTime }}</strong>
        </div>
        <p class="recovery-help">
          {{
            freezeEnded
              ? "冻结时间已结束，请重新登录。"
              : "可等待冻结结束，或联系管理员核实身份并获取重置码。"
          }}
        </p>
        <button
          class="button primary full"
          @click="changeMode(freezeEnded ? 'login' : 'recovery')"
        >
          {{ freezeEnded ? "重新登录" : "使用重置码恢复" }}
        </button>
        <button
          v-if="!freezeEnded"
          class="access-text-button"
          @click="changeMode('login')"
        >
          返回登录
        </button>
      </section>
      <template v-else-if="mode === 'recovery'">
        <h2>重置密码</h2>
        <p class="recovery-help">请先联系管理员核实身份，领取一次性重置码。</p>
        <form @submit.prevent="recover">
          <label class="field"
            >账号<input
              v-model.trim="form.login"
              required
              minlength="3"
              maxlength="80"
              autocomplete="username"
          /></label>
          <label class="field"
            >重置码<input
              v-model.trim="recovery.code"
              required
              minlength="8"
              maxlength="40"
              autocomplete="one-time-code"
              placeholder="管理员提供的重置码"
          /></label>
          <label class="field"
            >新密码<input
              v-model="recovery.password"
              type="password"
              required
              minlength="10"
              maxlength="128"
              autocomplete="new-password"
              placeholder="至少 10 个字符"
          /></label>
          <label class="field"
            >再次输入新密码<input
              v-model="recovery.confirm"
              type="password"
              required
              minlength="10"
              maxlength="128"
              autocomplete="new-password"
          /></label>
          <p v-if="error" class="field-error" role="alert">{{ error }}</p>
          <button class="button primary full" :disabled="busy">
            {{ busy ? "正在更新…" : "设置新密码" }}
          </button>
        </form>
        <button
          class="access-text-button"
          :disabled="busy"
          @click="changeMode('login')"
        >
          返回登录
        </button>
      </template>
      <template v-else>
        <h2>登录</h2>
        <p v-if="success" class="access-success" role="status">{{ success }}</p>
        <p
          v-if="notice && notice.code !== 'account_frozen'"
          class="recovery-help"
          role="status"
        >
          {{ notice.message }}
        </p>
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
        <div class="access-links">
          <span>账号由管理员开通</span
          ><button
            type="button"
            :disabled="busy"
            @click="changeMode('recovery')"
          >
            忘记密码
          </button>
        </div>
        <p class="recovery-help">
          默认保持登录 30 天。共用电脑使用后请退出账号。
        </p>
      </template>
      <details class="login-privacy">
        <summary>登录安全说明</summary>
        <p>
          为保护账号，我们记录登录时间、IP
          和随机浏览器标识，用于识别异常登录。登录环境记录用于最近 30
          天的安全核查，过期记录在后续登录时自动清理；冻结和恢复操作另存管理审计。不会读取电脑文件或浏览历史。
        </p>
      </details>
      <a class="client-download-entry" href="/download"
        ><Download :size="16" />下载 Windows 客户端</a
      >
    </main>
  </div>
</template>
<style scoped>
.access-links {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  margin-top: 18px;
  font-size: 13px;
  color: var(--muted);
}
.access-links button,
.access-text-button {
  border: 0;
  background: transparent;
  color: var(--green);
  cursor: pointer;
  font: inherit;
}
.access-text-button {
  display: block;
  margin: 18px auto 0;
  font-size: 14px;
}
.freeze-state {
  margin-top: 32px;
  text-align: center;
}
.security-symbol {
  width: 64px;
  height: 64px;
  display: grid;
  place-items: center;
  margin: 0 auto 20px;
  border-radius: 22px;
  background: #edf4fc;
  color: #0071e3;
}
.freeze-state h2 {
  font-size: 26px;
  margin: 0 0 12px;
}
.freeze-state p,
.recovery-help {
  font-size: 14px;
  color: var(--muted);
  line-height: 1.7;
}
.release-time {
  display: grid;
  gap: 8px;
  margin: 24px 0;
  padding: 18px;
  border-radius: 16px;
  background: #f5f5f7;
}
.release-time span {
  font-size: 12px;
  color: var(--muted);
}
.release-time strong {
  font-size: 22px;
  font-weight: 600;
}
.recovery-help {
  margin: 0 0 20px;
}
.access-success {
  font-size: 14px;
  color: #25704d;
}
.login-privacy {
  margin-top: 22px;
  font-size: 12px;
  color: var(--muted);
}
.login-privacy summary {
  cursor: pointer;
}
.login-privacy p {
  line-height: 1.7;
  margin: 10px 0 0;
}
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
