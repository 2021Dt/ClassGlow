<script setup lang="ts">
import { onMounted, ref } from "vue";
import { api, setSession } from "../api";
import { timeLabel } from "../domain";
type LoginSession = {
  id: string;
  label: string;
  createdAt: number;
  expiresAt: number;
  current: boolean;
};
const items = ref<LoginSession[]>([]),
  limit = ref(5),
  busy = ref(false),
  error = ref("");
async function load() {
  const result = await api<{ items: LoginSession[]; limit: number }>(
    "/account/sessions",
  );
  items.value = result.items;
  limit.value = result.limit;
}
async function change(item?: LoginSession) {
  busy.value = true;
  error.value = "";
  try {
    await api(
      item ? `/account/sessions/${item.id}` : "/account/sessions/logout-others",
      item ? "DELETE" : "POST",
    );
    if (item?.current) setSession("");
    else await load();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
onMounted(() =>
  load().catch((e) => {
    error.value = e.message;
  }),
);
</script>
<template>
  <section class="panel">
    <div class="section-heading">
      <h2>登录设备</h2>
      <button
        class="button secondary"
        :disabled="busy || items.length < 2"
        @click="change()"
      >
        退出其他设备
      </button>
    </div>
    <p class="muted">
      手机和电脑可同时登录，最多保留
      {{ limit }} 个登录设备。修改密码会退出全部设备。
    </p>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <article v-for="item in items" :key="item.id" class="record-row">
      <div class="section-heading">
        <strong
          >{{ item.label
          }}<span v-if="item.current" class="badge">当前设备</span></strong
        ><button
          class="text-button destructive"
          :disabled="busy"
          @click="change(item)"
        >
          退出此设备
        </button>
      </div>
      <p class="muted">
        {{
          item.createdAt
            ? `登录于 ${timeLabel(item.createdAt * 1000, true)}`
            : "升级前登录"
        }}
        · 有效期至 {{ timeLabel(item.expiresAt * 1000, true) }}
      </p>
    </article>
  </section>
</template>
