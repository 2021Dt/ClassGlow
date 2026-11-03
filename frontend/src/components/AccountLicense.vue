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
    notice.value = "高级许可已生效，你的身份和班级权限保持不变。";
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
        <h1>账号与高级许可</h1>
        <p class="muted">基础功能免费使用，高级许可由学校管理员提供。</p>
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
        <p class="muted">
          账号到期后暂停登录和发布。需要续期时联系系统管理员，原有数据会保留。
        </p>
      </article>
      <article class="panel">
        <KeyRound :size="22" />
        <h2>{{ premium ? "高级功能已开通" : "当前使用基础版" }}</h2>
        <strong>{{
          session.systemAdmin
            ? "系统管理员权限"
            : session.premiumUntil
              ? `许可截至 ${timeLabel(session.premiumUntil * 1000, true)}`
              : "基础功能永久免费"
        }}</strong>
        <p class="muted">
          高级许可到期后仍可使用基础功能，不删除作业、课表或定制方案。
        </p>
      </article>
    </div>
    <section v-if="!session.systemAdmin" class="panel license-redeem">
      <h2>输入高级许可码</h2>
      <p class="muted">
        请向系统管理员领取绑定本账号的许可码。续期从现有许可到期日继续计算；已到期则从现在开始。
      </p>
      <form @submit.prevent="redeem">
        <label class="field"
          >许可码<input
            v-model.trim="code"
            required
            maxlength="40"
            autocomplete="off"
            placeholder="管理员提供的高级许可码" /></label
        ><button class="button primary" :disabled="busy">
          {{ busy ? "正在开通…" : "开通 / 续期高级功能" }}
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
          <h3>高级功能 · 许可期内</h3>
          <p>自定义背景与卡片布局、多班批量发布、远程大屏控制。</p>
        </div>
      </div>
      <p class="muted small">
        许可只开放高级功能，不提升身份。班级管理仍需班主任权限，系统管理和签发许可仅限系统管理员。班级自定义展示使用最后发布该布局的账号许可，到期后恢复默认看板。
      </p>
    </section>
  </section>
</template>
