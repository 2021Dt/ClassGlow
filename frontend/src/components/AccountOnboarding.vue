<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { api } from "../api";
import { timeLabel } from "../domain";
const emit = defineEmits<{ changed: [] }>();
const form = reactive({ login: "", name: "", identity: "teacher", expiry: "" });
const classes = ref<any[]>([]),
  selected = reactive<Record<string, string[]>>({});
const selectedNames = reactive<Record<string, string>>({});
const search = ref(""),
  total = ref(0),
  busy = ref(false),
  error = ref(""),
  result = ref<any>(null),
  copied = ref(false);
const teacher = computed(() => form.identity === "teacher");
async function findClasses() {
  try {
    const r = await api(
      `/system/classes?limit=100&search=${encodeURIComponent(search.value)}`,
    );
    classes.value = r.items.filter((c: any) => !c.archived);
    total.value = r.total;
  } catch (e) {
    error.value = (e as Error).message;
  }
}
onMounted(findClasses);
watch(
  () => form.identity,
  () => {
    for (const key of Object.keys(selected)) delete selected[key];
  },
);
function toggle(c: any, enabled: boolean) {
  if (enabled) {
    selected[c.id] = [];
    selectedNames[c.id] = c.name;
  } else delete selected[c.id];
}
async function create() {
  busy.value = true;
  error.value = "";
  copied.value = false;
  try {
    result.value = await api("/system/account-invitations", "POST", {
      login: form.login,
      name: form.name,
      identity: form.identity,
      expiresAt: form.expiry
        ? new Date(`${form.expiry}T23:59:59+08:00`).getTime() / 1000
        : 0,
      classes: Object.entries(selected).map(([classId, subjects]) => ({
        classId,
        subjects,
      })),
    });
    emit("changed");
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    busy.value = false;
  }
}
async function copy() {
  try {
    await navigator.clipboard.writeText(
      `账号：${result.value.login}\n激活码：${result.value.activationCode}\n请打开 ${location.origin} 选择“首次激活”，设置自己的密码。`,
    );
    copied.value = true;
  } catch {
    error.value = "浏览器未允许复制，请手动选中账号和激活码复制。";
  }
}
function again() {
  result.value = null;
  form.login = form.name = "";
  for (const k of Object.keys(selected)) delete selected[k];
}
</script>
<template>
  <section class="panel onboarding-form">
    <template v-if="result"
      ><span class="badge neutral">账号已创建 · 等待本人激活</span>
      <h2>{{ result.name }}的激活信息</h2>
      <p>请将以下信息交给这位老师，密码由老师首次激活时自行设置。</p>
      <div class="issued-code">
        <span>账号</span><strong>{{ result.login }}</strong
        ><span>一次性激活码</span><code>{{ result.activationCode }}</code
        ><small
          >请在
          {{
            timeLabel(result.expiresAt * 1000, true)
          }}
          前激活。离开此页后不再显示原码，可从账号列表重发。</small
        >
      </div>
      <div class="record-actions">
        <button class="button primary" @click="copy">
          {{ copied ? "已复制" : "复制账号与激活码" }}</button
        ><button class="button secondary" @click="again">继续开通账号</button>
      </div></template
    >
    <template v-else
      ><h2>为老师开通账号</h2>
      <p class="muted">
        先安排身份和任教范围，再把一次性激活码交给本人。无需替老师设置密码。
      </p>
      <form @submit.prevent="create">
        <fieldset :disabled="busy">
          <div class="form-grid">
            <label class="field"
              >老师姓名<input
                v-model.trim="form.name"
                required
                maxlength="40"
                placeholder="例如：张老师" /></label
            ><label class="field"
              >登录账号<input
                v-model.trim="form.login"
                required
                minlength="3"
                maxlength="80"
                pattern="[a-zA-Z0-9_@.\-]+"
                placeholder="字母、数字或邮箱"
            /></label>
          </div>
          <label class="field"
            >账号身份<select v-model="form.identity">
              <option value="teacher">普通老师 · 维护任教学科</option>
              <option value="headteacher">班主任 · 管理班级和教室</option>
              <option value="system_admin">系统管理员 · 全站最高权限</option>
            </select></label
          >
          <label class="field"
            >账号到期日（北京时间，留空为长期有效）<input
              type="date"
              v-model="form.expiry"
          /></label>
          <p class="muted small">
            账号期限和高级许可期限分别管理。新账号默认使用免费基础功能，高级许可请到“高级许可”签发。
          </p>
          <template v-if="form.identity !== 'system_admin'">
            <h3>
              {{
                teacher
                  ? "分配班级和学科"
                  : "选择管理的班级（也可稍后自行建班）"
              }}
            </h3>
            <div class="system-search">
              <input
                v-model="search"
                aria-label="搜索待分配班级"
                placeholder="搜索班级"
              /><button
                type="button"
                class="button secondary"
                @click="findClasses"
              >
                搜索
              </button>
            </div>
            <div class="grant-list">
              <p v-if="!classes.length" class="muted">
                暂无可分配班级。可以先开通班主任账号，由班主任创建班级。
              </p>
              <article v-for="c in classes" :key="c.id">
                <label class="system-check"
                  ><input
                    type="checkbox"
                    :checked="selected[c.id] !== undefined"
                    @change="
                      toggle(c, ($event.target as HTMLInputElement).checked)
                    "
                  />{{ c.name }}</label
                >
                <div v-if="teacher && selected[c.id]" class="subject-grants">
                  <label v-for="subject in c.subjects" :key="subject"
                    ><input
                      type="checkbox"
                      v-model="selected[c.id]"
                      :value="subject"
                    />{{ subject }}</label
                  >
                </div>
              </article>
            </div>
            <p v-if="total > 100" class="muted small">
              这里只显示前 100 项，请输入班级名称缩小范围。
            </p>
            <p class="muted small">
              已选：{{
                Object.keys(selected)
                  .map((id) => selectedNames[id])
                  .join("、") || "尚未选择"
              }}
            </p>
          </template>
          <p v-else class="notice warning">
            系统管理员可管理全部账号、班级、许可和数据，请仅分配给可信任的学校负责人。
          </p>
          <button class="button primary" :disabled="busy">
            {{ busy ? "正在创建…" : "创建账号并生成激活码" }}
          </button>
        </fieldset>
      </form>
    </template>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
  </section>
</template>
