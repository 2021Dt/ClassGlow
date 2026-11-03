<script setup lang="ts">
import { onMounted, ref } from "vue";
import { api } from "../api";
import { timeLabel } from "../domain";
const props = defineProps<{ initialUserId?: string }>();
const emit = defineEmits<{ changed: [] }>();
const query = ref(""),
  users = ref<any[]>([]),
  selected = ref<any>(null),
  records = ref<any[]>([]);
const days = ref(30),
  issued = ref<any>(null),
  busy = ref(false),
  error = ref(""),
  notice = ref("");
async function search() {
  users.value = (
    await api(
      `/system/users?limit=100&search=${encodeURIComponent(query.value)}`,
    )
  ).items.filter((u: any) => !u.systemAdmin);
}
async function choose(user: any) {
  selected.value = user;
  issued.value = null;
  records.value = (await api(`/system/licenses?userId=${user.id}`)).items;
}
async function run(fn: () => Promise<void>) {
  busy.value = true;
  error.value = notice.value = "";
  try {
    await fn();
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
onMounted(() =>
  run(async () => {
    await search();
    if (props.initialUserId) {
      const u = await api(`/system/users/${props.initialUserId}`);
      await choose(u);
    }
  }),
);
async function issue() {
  await run(async () => {
    issued.value = await api("/system/licenses", "POST", {
      userId: selected.value.id,
      days: days.value,
    });
    records.value = (
      await api(`/system/licenses?userId=${selected.value.id}`)
    ).items;
    emit("changed");
  });
}
async function revoke(id: string) {
  if (!confirm("作废这个尚未兑换的许可码？")) return;
  await run(async () => {
    await api(`/system/licenses/${id}/revoke`, "POST");
    await choose(selected.value);
    emit("changed");
  });
}
async function endLicense() {
  if (
    !confirm(
      `终止 ${selected.value.name} 的高级许可，并作废其未兑换许可码？基础功能和教学数据保留。`,
    )
  )
    return;
  await run(async () => {
    await api(`/system/users/${selected.value.id}/license/revoke`, "POST");
    await choose(await api(`/system/users/${selected.value.id}`));
    emit("changed");
    notice.value = "高级许可已终止，基础功能仍可使用。";
  });
}
async function copy() {
  try {
    await navigator.clipboard.writeText(
      `账号：${issued.value.login}\n高级许可码：${issued.value.code}\n许可时长：${issued.value.days} 天\n登录后进入“账号与许可”兑换。`,
    );
    notice.value = "已复制，可交给该账号使用者。";
  } catch {
    error.value = "浏览器未允许复制，请手动选中许可码复制。";
  }
}
</script>
<template>
  <section class="panel license-manager">
    <h2>高级许可管理</h2>
    <p class="muted">
      许可码绑定指定账号，一次兑换。续期会接续现有许可，不改变账号到期日和身份权限。
    </p>
    <form class="system-search" @submit.prevent="run(search)">
      <label class="sr-only" for="license-search">搜索许可账号</label
      ><input
        id="license-search"
        v-model="query"
        placeholder="输入老师姓名或账号"
      /><button class="button secondary" :disabled="busy">查找账号</button>
    </form>
    <label class="field"
      >选择账号<select
        :value="selected?.id || ''"
        :disabled="busy"
        @change="
          run(() =>
            choose(
              users.find(
                (u) => u.id === ($event.target as HTMLSelectElement).value,
              ),
            ),
          )
        "
      >
        <option disabled value="">请选择班主任或老师</option>
        <option v-for="u in users" :key="u.id" :value="u.id">
          {{ u.name }} · {{ u.login }}{{ !u.active ? "（已停用）" : "" }}
        </option>
        <option
          v-if="selected && !users.some((u) => u.id === selected.id)"
          :value="selected.id"
        >
          {{ selected.name }} · {{ selected.login }}
        </option>
      </select></label
    >
    <template v-if="selected"
      ><div class="notice subtle">
        <span
          >账号：{{
            selected.expiresAt
              ? timeLabel(selected.expiresAt * 1000, true) + " 到期"
              : "长期有效"
          }}<br />高级许可：{{
            selected.premiumUntil
              ? timeLabel(selected.premiumUntil * 1000, true) +
                (selected.premiumActive ? " 到期" : "（已到期）")
              : "尚未开通"
          }}</span
        >
      </div>
      <form class="license-issue" @submit.prevent="issue">
        <label class="field"
          >高级许可天数<input
            type="number"
            v-model.number="days"
            min="1"
            max="3660"
            required /></label
        ><button class="button primary" :disabled="busy || !selected.active">
          生成专属许可码</button
        ><button
          v-if="selected.premiumActive"
          type="button"
          class="text-button destructive"
          :disabled="busy"
          @click="endLicense"
        >
          终止高级许可
        </button>
      </form>
      <div v-if="issued" class="issued-code">
        <span>{{ issued.login }} · {{ issued.days }} 天高级许可</span
        ><code>{{ issued.code }}</code
        ><small
          >请在
          {{ timeLabel(issued.expiresAt * 1000, true) }}
          前兑换。明文仅本次显示。</small
        ><button class="button secondary" @click="copy">复制许可信息</button>
      </div>
      <h3>最近签发记录</h3>
      <p v-if="!records.length" class="muted">尚未签发许可码。</p>
      <article v-for="r in records" :key="r.id" class="system-row">
        <div>
          <strong>{{ r.days }} 天许可</strong>
          <p class="muted small">
            {{
              r.usedAt
                ? "已兑换 · " + timeLabel(r.usedAt * 1000, true)
                : r.revoked
                  ? "已作废"
                  : r.expired
                    ? "兑换期限已过"
                    : "等待兑换 · " +
                      timeLabel(r.expiresAt * 1000, true) +
                      " 前有效"
            }}
          </p>
        </div>
        <button
          v-if="!r.usedAt && !r.revoked && !r.expired"
          class="text-button destructive"
          :disabled="busy"
          @click="revoke(r.id)"
        >
          作废此码
        </button>
      </article>
    </template>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <p v-if="notice" class="notice subtle" role="status">{{ notice }}</p>
  </section>
</template>
