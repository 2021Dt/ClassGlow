<script setup lang="ts">
import { reactive, ref } from "vue";
import { School, ShieldCheck, ArrowRight } from "@lucide/vue";
import { api } from "../api";

const emit = defineEmits<{ initialized: [string] }>();
const form = reactive({
  setupKey: "",
  siteName: "班级看板",
  login: "",
  name: "系统管理员",
  password: "",
  registrationOpen: false,
});
const confirmation = ref(""),
  busy = ref(false),
  error = ref("");
async function initialize() {
  error.value = "";
  if (confirmation.value !== form.password) {
    error.value = "两次输入的口令不一致。";
    return;
  }
  busy.value = true;
  try {
    const result = await api("/setup/initialize", "POST", form);
    form.setupKey = form.password = confirmation.value = "";
    emit("initialized", result.token);
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="setup-shell">
    <aside class="setup-intro">
      <div class="brand">
        <span class="brand-mark"><School :size="24" /></span>班级看板
      </div>
      <p class="eyebrow">首次安装 · 只需一次</p>
      <h1>让你的学校，<br />拥有自己的看板。</h1>
      <p class="muted">
        设置站点名称，创建首个系统管理员，然后开始管理教师、班级和教室设备。
      </p>
      <ol class="setup-steps">
        <li>从服务器读取安装密钥</li>
        <li>创建系统管理员</li>
        <li>进入后台，添加教师和班级</li>
      </ol>
      <div class="notice subtle">
        <ShieldCheck :size="20" /><span
          >初始化完成后，安装入口自动锁定。现有班级和教师数据会保留。</span
        >
      </div>
    </aside>
    <main class="panel setup-card">
      <h2>初始化服务器</h2>
      <details class="setup-help">
        <summary>如何获取安装密钥</summary>
        <p>在服务器项目目录执行：</p>
        <code
          >docker compose --env-file deploy/.env -f compose.production.yaml exec
          api python -m scripts.setup_key</code
        >
        <p>本机开发：</p>
        <code>uv run python -m scripts.setup_key</code>
      </details>
      <form @submit.prevent="initialize">
        <label class="field"
          >安装密钥<input
            v-model.trim="form.setupKey"
            type="password"
            required
            autocomplete="off"
        /></label>
        <label class="field"
          >站点名称<input v-model.trim="form.siteName" required maxlength="80"
        /></label>
        <div class="form-grid">
          <label class="field"
            >管理员账号<input
              v-model.trim="form.login"
              required
              minlength="3"
              maxlength="80"
              autocomplete="username"
              pattern="[a-zA-Z0-9_@.\-]+"
          /></label>
          <label class="field"
            >管理员姓名<input v-model.trim="form.name" required maxlength="40"
          /></label>
        </div>
        <label class="field"
          >管理员口令<input
            v-model="form.password"
            type="password"
            required
            minlength="10"
            maxlength="128"
            autocomplete="new-password"
        /></label>
        <label class="field"
          >再次输入口令<input
            v-model="confirmation"
            type="password"
            required
            autocomplete="new-password"
        /></label>
        <p class="muted small">
          若使用已有账号，请填写该账号原口令，将其升级为系统管理员。
        </p>
        <label class="system-check"
          ><input
            type="checkbox"
            v-model="form.registrationOpen"
          />允许教师自行注册</label
        >
        <p class="muted small">
          关闭时，由系统管理员创建教师账号；以后可以在后台更改。
        </p>
        <p v-if="error" class="field-error" role="alert">{{ error }}</p>
        <button class="button primary full" :disabled="busy">
          {{ busy ? "正在初始化…" : "完成初始化并进入后台"
          }}<ArrowRight :size="18" />
        </button>
      </form>
    </main>
  </div>
</template>
