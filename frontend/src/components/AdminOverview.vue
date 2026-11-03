<script setup lang="ts">
import { computed } from "vue";
import {
  Users,
  School,
  Monitor,
  KeyRound,
  ArrowUpRight,
  ArrowRight,
  UserPlus,
  ShieldCheck,
  Clock3,
  Check,
  CircleHelp,
} from "@lucide/vue";
import { adminDate, auditNames } from "../admin";
const props = defineProps<{ overview: any; activity: any[] }>();
const emit = defineEmits<{
  navigate: [string, Record<string, string>?];
  createAccount: [];
  createClass: [];
}>();
const stats = computed(() => [
  {
    label: "教师与管理员",
    count: props.overview.users,
    note: `${props.overview.availableUsers} 个账号可正常登录`,
    icon: Users,
    page: "users",
    tone: "blue",
  },
  {
    label: "使用中的班级",
    count: props.overview.classes,
    note: `${props.overview.archivedClasses} 个班级已归档`,
    icon: School,
    page: "classes",
    tone: "purple",
  },
  {
    label: "在线教室",
    count: props.overview.onlineDevices,
    note: `共 ${props.overview.devices} 台已绑定设备`,
    icon: Monitor,
    page: "devices",
    tone: "green",
  },
  {
    label: "高级许可账号",
    count: props.overview.premiumUsers,
    note: "当前账号与许可均在有效期内",
    icon: KeyRound,
    page: "licenses",
    tone: "amber",
  },
]);
const tasks = computed<
  {
    label: string;
    note: string;
    count: number;
    page: string;
    filter: Record<string, string>;
  }[]
>(() => [
  {
    label: "账号即将到期",
    note: "未来 7 天内到期，按需延长使用期限",
    count: props.overview.expiringAccounts,
    page: "users",
    filter: { status: "expiring", license: "" },
  },
  {
    label: "高级许可即将到期",
    note: "未来 7 天内到期，可提前发放续期码",
    count: props.overview.expiringLicenses,
    page: "users",
    filter: { license: "expiring", status: "" },
  },
  {
    label: "离线教室设备",
    note: "检查教室电脑是否开机、客户端是否运行",
    count: props.overview.devices - props.overview.onlineDevices,
    page: "devices",
    filter: { status: "offline", license: "" },
  },
]);
const onlinePercent = computed(() =>
  props.overview.devices
    ? Math.round((props.overview.onlineDevices / props.overview.devices) * 100)
    : 0,
);
</script>
<template>
  <div class="admin-metrics">
    <button
      v-for="s in stats"
      :key="s.label"
      class="admin-metric"
      @click="emit('navigate', s.page)"
    >
      <span class="metric-heading"
        >{{ s.label
        }}<span class="metric-icon" :class="s.tone"
          ><component :is="s.icon" :size="18" /></span
      ></span>
      <strong>{{ s.count }}<ArrowUpRight :size="18" /></strong
      ><small>{{ s.note }}</small>
    </button>
  </div>
  <section class="admin-quickstart">
    <div>
      <span class="admin-mini-label">日常管理</span>
      <h2>让每位老师，准备就绪。</h2>
      <p>添加账号、分配班级，让教学安排有序同步。</p>
    </div>
    <div class="quickstart-actions">
      <button class="button primary" @click="emit('createAccount')">
        <UserPlus :size="17" />添加老师</button
      ><button class="button secondary" @click="emit('navigate', 'licenses')">
        <KeyRound :size="17" />发放许可</button
      ><button class="button secondary" @click="emit('createClass')">
        <School :size="17" />创建班级
      </button>
    </div>
  </section>
  <div class="admin-overview-grid">
    <section class="admin-card admin-attention">
      <header class="admin-card-heading">
        <div>
          <h2>需要关注</h2>
          <p>及时处理，减少课堂中断</p>
        </div>
        <span class="admin-pill amber"><Clock3 :size="13" />待办提醒</span>
      </header>
      <button
        v-for="task in tasks"
        :key="task.label"
        class="admin-task"
        @click="emit('navigate', task.page, task.filter)"
      >
        <span class="task-icon" :class="task.count ? 'amber' : 'green'"
          ><component :is="task.count ? Clock3 : Check" :size="18"
        /></span>
        <span
          ><strong>{{ task.label }}</strong
          ><small>{{ task.note }}</small></span
        ><b>{{ task.count }}</b
        ><ArrowRight :size="16" />
      </button>
      <p class="admin-card-footnote">
        <CircleHelp :size="14" />离线不一定是故障，教室关机时会显示离线。
      </p>
    </section>
    <section class="admin-card admin-device-summary">
      <header class="admin-card-heading">
        <div>
          <h2>教室连接状态</h2>
          <p>最近 90 秒内有联系视为在线</p>
        </div>
        <Monitor :size="19" />
      </header>
      <div class="device-connection">
        <strong
          >{{ overview.onlineDevices
          }}<small> / {{ overview.devices }}</small></strong
        ><span>教室设备在线</span>
      </div>
      <div
        class="connection-track"
        role="meter"
        aria-label="教室设备在线比例"
        :aria-valuenow="onlinePercent"
        aria-valuemin="0"
        aria-valuemax="100"
      >
        <span :style="{ width: onlinePercent + '%' }"></span>
      </div>
      <div class="connection-legend">
        <span><i></i>在线 {{ overview.onlineDevices }}</span
        ><span
          ><i class="offline"></i>离线
          {{ overview.devices - overview.onlineDevices }}</span
        >
      </div>
      <button
        class="button secondary full"
        @click="emit('navigate', 'devices')"
      >
        查看教室设备<ArrowUpRight :size="15" />
      </button>
    </section>
    <section class="admin-card admin-activity">
      <header class="admin-card-heading">
        <div>
          <h2>最近操作</h2>
          <p>账号、许可与学校管理的重要变更</p>
        </div>
        <button class="text-button" @click="emit('navigate', 'audit')">
          全部记录<ArrowRight :size="15" />
        </button>
      </header>
      <div v-if="!activity.length" class="admin-empty compact">
        <ShieldCheck :size="26" />
        <h3>操作记录将显示在这里</h3>
        <p>管理员的关键操作会自动记录，便于追溯。</p>
      </div>
      <div v-for="item in activity" :key="item.id" class="admin-activity-row">
        <span class="activity-marker"></span
        ><span
          ><strong>{{ auditNames[item.action] ?? item.action }}</strong
          ><small
            >{{ item.actor
            }}<span v-if="item.target"> · {{ item.target }}</span></small
          ></span
        ><time>{{ adminDate(item.at, true) }}</time>
      </div>
    </section>
    <section class="admin-card admin-role-summary">
      <header class="admin-card-heading">
        <div>
          <h2>人员分布</h2>
          <p>根据职责分配操作权限</p>
        </div>
        <Users :size="19" />
      </header>
      <button
        v-for="role in [
          { key: 'admins', name: '系统管理员', id: 'system_admin' },
          { key: 'headteachers', name: '班主任', id: 'headteacher' },
          { key: 'teachers', name: '普通老师', id: 'teacher' },
        ]"
        :key="role.key"
        @click="emit('navigate', 'users', { identity: role.id })"
      >
        <span>{{ role.name }}</span
        ><strong
          >{{ overview.roles[role.key] }}<ArrowRight :size="14"
        /></strong>
      </button>
      <p class="admin-card-footnote">许可只开放高级功能，不改变身份权限。</p>
    </section>
  </div>
</template>
