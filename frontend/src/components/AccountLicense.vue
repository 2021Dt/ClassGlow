<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { ShieldCheck, KeyRound } from "@lucide/vue";
import { api, premiumAvailable, session, updateProfile } from "../api";
import { timeLabel } from "../domain";
const emit = defineEmits<{ changed: [] }>();
const code = ref(""),
  busy = ref(false),
  error = ref(""),
  notice = ref("");
const now = ref(Date.now());
const premium = computed(() => premiumAvailable(now.value));
let timer: ReturnType<typeof setInterval>;
async function refresh() {
  try {
    updateProfile(await api("/account/license"));
  } catch (e) {
    error.value = (e as Error).message;
  }
}
onMounted(() => {
  refresh();
  timer = setInterval(() => (now.value = Date.now()), 5000);
});
onUnmounted(() => clearInterval(timer));
async function redeem() {
  busy.value = true;
  error.value = notice.value = "";
  try {
    updateProfile(
      await api("/account/license/redeem", "POST", { code: code.value }),
    );
    code.value = "";
    notice.value = "高级功能已开通，可以开始使用了。";
    emit("changed");
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
</script>
<template>
  <section class="license-page">
    <header class="page-heading">
      <div>
        <p class="eyebrow">{{ session.name }} · 我的账号</p>
        <h1>我的账号</h1>
      </div>
    </header>
    <div class="license-summary">
      <article class="panel">
        <ShieldCheck :size="22" />
        <h2>账号有效期</h2>
        <strong>{{
          session.expiresAt
            ? timeLabel(session.expiresAt * 1000, true)
            : "长期有效"
        }}</strong>
        <p class="muted">续期请联系管理员</p>
      </article>
      <article class="panel">
        <KeyRound :size="22" />
        <h2>{{ premium ? "高级功能已开通" : "当前使用基础版" }}</h2>
        <strong>{{
          session.systemAdmin
            ? "系统管理员权限"
            : session.premiumUntil
              ? `有效期至 ${timeLabel(session.premiumUntil * 1000, true)}`
              : "基础功能永久免费"
        }}</strong>
        <p class="muted">许可到期后恢复基础版</p>
      </article>
    </div>
    <section v-if="!session.systemAdmin" class="panel license-redeem">
      <h2>开通高级功能</h2>
      <form @submit.prevent="redeem">
        <label class="field"
          >激活码<input
            v-model.trim="code"
            required
            maxlength="40"
            autocomplete="off"
            placeholder="输入管理员给你的激活码" /></label
        ><button class="button primary" :disabled="busy">
          {{ busy ? "正在开通…" : "确认开通" }}
        </button>
      </form>
      <p v-if="error" class="field-error" role="alert">{{ error }}</p>
      <p v-if="notice" class="notice subtle" role="status">{{ notice }}</p>
    </section>
    <section class="panel">
      <h2>功能范围</h2>
      <div class="license-features">
        <div>
          <h3>基础功能 · 免费</h3>
          <p>作业、提醒、课表、默认看板、设备绑定和自动同步。</p>
        </div>
        <div>
          <h3>高级功能</h3>
          <p>自定义背景与卡片布局、多班批量发布、远程大屏控制。</p>
        </div>
      </div>
    </section>
  </section>
</template>
