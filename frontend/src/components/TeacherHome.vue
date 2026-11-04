<script setup lang="ts">
import { computed, ref } from "vue";
import {
  Plus,
  Bell,
  Palette,
  Monitor,
  ArrowRight,
  BookOpen,
  CalendarDays,
  FilePenLine,
  CheckCircle2,
  CircleHelp,
} from "@lucide/vue";
import {
  timeLabel,
  type Classroom,
  type Content,
  type Device,
  type Draft,
  type Kind,
} from "../domain";
import { deviceStatus } from "../deviceStatus";
const props = defineProps<{
  classroom: Classroom;
  contents: Content[];
  devices: Device[];
  drafts: Draft[];
  canManage: boolean;
  userId: string;
}>();
const emit = defineEmits<{
  compose: [Kind];
  edit: [Content];
  records: [];
  drafts: [];
  timetable: [];
  devices: [id?: string];
  design: [];
  guide: [];
}>();
const kind = ref<Kind>("homework");
const shown = computed(() =>
  props.contents
    .filter((c) => c.kind === kind.value)
    .sort((a, b) => b.created.localeCompare(a.created)),
);
const online = computed(() => props.devices.filter((d) => d.online).length);
const myDrafts = computed(() =>
  props.drafts.filter((d) => d.classIds.includes(props.classroom.id)),
);
</script>

<template>
  <div class="teaching-home">
    <div class="teacher-guide-entry">
      <div>
        <strong>从发布到教室展示</strong
        ><span>选班级 → 发作业 → 查看教室电脑</span>
      </div>
      <button class="button secondary" @click="emit('guide')">
        <CircleHelp :size="17" />跟着做一次
      </button>
    </div>
    <div
      class="teaching-actions"
      :class="{ 'three-actions': !canManage }"
      aria-label="常用操作"
    >
      <button
        class="teaching-action main-action"
        data-tour="publish"
        :disabled="classroom.archived"
        @click="emit('compose', 'homework')"
      >
        <span class="action-symbol"><Plus :size="23" /></span
        ><strong>发作业</strong><ArrowRight :size="17" />
      </button>
      <button
        v-if="canManage"
        class="teaching-action"
        :disabled="classroom.archived"
        @click="emit('compose', 'todo')"
      >
        <span class="action-symbol amber"><Bell :size="21" /></span
        ><strong>发提醒</strong><ArrowRight :size="16" />
      </button>
      <button
        v-if="canManage"
        class="teaching-action"
        data-tour="design"
        @click="emit('design')"
      >
        <span class="action-symbol violet"><Palette :size="21" /></span
        ><strong>设计看板</strong><ArrowRight :size="16" />
      </button>
      <button
        v-if="canManage"
        class="teaching-action"
        data-tour="devices"
        @click="emit('devices')"
      >
        <span class="action-symbol blue"><Monitor :size="21" /></span
        ><strong>教室电脑</strong><ArrowRight :size="16" />
      </button>
      <button
        v-if="!canManage"
        class="teaching-action"
        data-tour="records"
        @click="emit('records')"
      >
        <span class="action-symbol blue"><BookOpen :size="21" /></span
        ><strong>发布记录</strong><ArrowRight :size="16" />
      </button>
      <button v-if="!canManage" class="teaching-action" @click="emit('drafts')">
        <span class="action-symbol violet"><FilePenLine :size="21" /></span
        ><strong>草稿箱</strong><ArrowRight :size="16" />
      </button>
    </div>
    <div class="class-connection" :class="{ disconnected: !devices.length }">
      <span class="connection-led" :class="{ connected: online > 0 }"></span>
      <span>{{
        !devices.length
          ? "还没有连接教室电脑"
          : online
            ? `${online} 台教室电脑在线`
            : "教室电脑暂时离线"
      }}</span>
      <button @click="emit('devices')">
        {{
          devices.length
            ? "查看当前看板"
            : canManage
              ? "连接电脑"
              : "查看连接状态"
        }}<ArrowRight :size="14" />
      </button>
    </div>
    <div
      v-if="devices.length"
      class="home-device-status"
      aria-label="各电脑展示状态"
    >
      <button
        v-for="device in devices"
        :key="device.id"
        @click="emit('devices', device.id)"
      >
        <Monitor :size="17" /><strong>{{ device.name }}</strong
        ><span>{{ deviceStatus(device, classroom.version) }}</span>
      </button>
    </div>
    <div v-if="myDrafts.length" class="resume-draft">
      <FilePenLine :size="17" /><span
        >还有 {{ myDrafts.length }} 条草稿未发布</span
      ><button @click="emit('drafts')">
        继续编辑<ArrowRight :size="14" />
      </button>
    </div>
    <div class="teaching-grid">
      <section class="teaching-card" aria-labelledby="current-posts">
        <header>
          <h2 id="current-posts">正在展示</h2>
          <button
            class="text-button"
            data-tour="records"
            @click="emit('records')"
          >
            全部记录<ArrowRight :size="14" />
          </button>
        </header>
        <div class="post-tabs" role="tablist" aria-label="展示内容类型">
          <button
            role="tab"
            :aria-selected="kind === 'homework'"
            @click="kind = 'homework'"
          >
            作业
            <span>{{
              contents.filter((c) => c.kind === "homework").length
            }}</span>
          </button>
          <button
            role="tab"
            :aria-selected="kind === 'todo'"
            @click="kind = 'todo'"
          >
            提醒
            <span>{{ contents.filter((c) => c.kind === "todo").length }}</span>
          </button>
        </div>
        <div v-if="!shown.length" class="teaching-empty">
          <CheckCircle2 :size="32" /><strong>{{
            kind === "homework" ? "暂无正在展示的作业" : "暂无正在展示的提醒"
          }}</strong
          ><button
            v-if="kind === 'homework' || canManage"
            class="text-button"
            :disabled="classroom.archived"
            @click="emit('compose', kind)"
          >
            {{ kind === "homework" ? "发布第一条作业" : "发布一条提醒"
            }}<Plus :size="15" />
          </button>
        </div>
        <div v-else class="teaching-scroll" role="tabpanel">
          <article v-for="item in shown" :key="item.id" class="teaching-post">
            <div>
              <strong class="post-subject">{{ item.subject }}</strong
              ><span>{{ item.author }}</span
              ><button
                v-if="
                  !classroom.archived && (canManage || item.authorId === userId)
                "
                class="text-button"
                @click="emit('edit', item)"
              >
                修改
              </button>
            </div>
            <p>{{ item.body }}</p>
            <small>展示至 {{ timeLabel(item.until, true) }}</small>
          </article>
        </div>
      </section>
      <section class="teaching-card" aria-labelledby="today-schedule">
        <header>
          <h2 id="today-schedule"><CalendarDays :size="18" />今日课表</h2>
          <button
            v-if="canManage"
            class="text-button"
            @click="emit('timetable')"
          >
            编辑课表
          </button>
        </header>
        <div v-if="!classroom.timetable.length" class="teaching-empty">
          <CalendarDays :size="32" /><strong>{{
            classroom.timetableState === "configured"
              ? "今天没有课程"
              : "还没有设置课表"
          }}</strong
          ><button
            v-if="canManage && classroom.timetableState !== 'configured'"
            class="text-button"
            @click="emit('timetable')"
          >
            设置课表<ArrowRight :size="14" />
          </button>
        </div>
        <ol v-else class="teaching-scroll today-lessons">
          <li
            v-for="(subject, index) in classroom.timetable"
            :key="index"
            :class="{ 'lesson-current': index === classroom.currentPeriod }"
          >
            <span>{{ String(index + 1).padStart(2, "0") }}</span
            ><strong>{{ subject }}</strong
            ><small v-if="index === classroom.currentPeriod">进行中</small>
          </li>
        </ol>
        <p v-if="classroom.nextPeriod" class="next-lesson">
          {{ classroom.nextPeriod }}
        </p>
      </section>
    </div>
  </div>
</template>

<style scoped>
.teaching-actions {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
  margin-bottom: 20px;
}
.teaching-actions.three-actions {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
.teaching-action {
  display: flex;
  align-items: center;
  gap: 12px;
  text-align: left;
  padding: 22px 18px;
  border: 1px solid #e4eae6;
  border-radius: 17px;
  background: #fff;
  color: #293d35;
  min-height: 92px;
}
.teaching-action strong {
  font-size: 15px;
  white-space: nowrap;
}
.teaching-action > svg {
  margin-left: auto;
  color: #98a49d;
}
.teaching-action:hover {
  border-color: #9ebdab;
  box-shadow: 0 5px 16px #263a3208;
}
.main-action {
  background: #2b6654;
  color: white;
  border-color: #2b6654;
}
.main-action > svg {
  color: #ffffffb5;
}
.action-symbol {
  width: 38px;
  height: 38px;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  border-radius: 12px;
  background: #ffffff20;
}
.action-symbol.amber {
  color: #ad813e;
  background: #faf1df;
}
.action-symbol.violet {
  color: #8572a8;
  background: #f2edf8;
}
.action-symbol.blue {
  color: #5d83ae;
  background: #eaf2fb;
}
.class-connection,
.resume-draft {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 14px 17px;
  border: 1px solid #e4eae6;
  border-radius: 13px;
  margin-bottom: 18px;
  background: #ffffff80;
  font-size: 13px;
  color: #64786c;
}
.class-connection.disconnected {
  background: #fbf8ef;
  border-color: #ede7d8;
  color: #8c7551;
}
.connection-led {
  height: 7px;
  width: 7px;
  border-radius: 50%;
  background: #b5bdb9;
}
.connection-led.connected {
  background: #51a67d;
}
.class-connection button,
.resume-draft button {
  display: inline-flex;
  gap: 7px;
  align-items: center;
  margin-left: auto;
  padding: 4px 0 4px 10px;
  background: none;
  color: #2b6654;
  font-size: 12px;
}
.teaching-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.45fr) minmax(0, 1fr);
  gap: 22px;
}
.teaching-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  height: 440px;
  background: white;
  border: 1px solid #e5ebe6;
  border-radius: 18px;
  overflow: hidden;
}
.teaching-card > header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 22px 23px 18px;
  flex-shrink: 0;
}
.teaching-card h2 {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 16px;
}
.post-tabs {
  display: flex;
  gap: 6px;
  padding: 0 22px 16px;
  flex-shrink: 0;
}
.post-tabs button {
  padding: 6px 14px;
  border-radius: 9px;
  background: transparent;
  color: #758179;
  font-size: 13px;
}
.post-tabs button[aria-selected="true"] {
  background: #edf4ef;
  color: #2b6654;
  font-weight: 600;
}
.post-tabs span {
  margin-left: 5px;
  font-size: 11px;
  opacity: 0.7;
}
.teaching-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  padding: 0 23px 20px;
  margin: 0;
}
.teaching-empty {
  display: flex;
  flex: 1;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 14px;
  color: #9ca8a0;
  padding: 20px 12px 46px;
  text-align: center;
}
.teaching-empty strong {
  color: #7b897f;
  font-size: 13px;
  font-weight: 400;
}
.teaching-post {
  padding: 18px 0;
  border-bottom: 1px solid #edf0ed;
}
.teaching-post:first-child {
  padding-top: 2px;
}
.teaching-post:last-child {
  border: 0;
}
.teaching-post > div {
  display: flex;
  align-items: center;
  gap: 10px;
}
.post-subject {
  background: #eef3f0;
  border-radius: 6px;
  padding: 3px 8px;
  font-size: 12px;
  font-weight: 500;
}
.teaching-post > div > span {
  color: #8a958e;
  font-size: 11px;
}
.teaching-post button {
  margin-left: auto;
}
.teaching-post p {
  margin: 12px 0 9px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.8;
  font-size: 14px;
  color: #3c4941;
}
.teaching-post small {
  color: #9aa39d;
  font-size: 11px;
}
.today-lessons {
  list-style: none;
}
.today-lessons li {
  display: flex;
  align-items: center;
  padding: 14px 10px;
  gap: 16px;
  border-bottom: 1px solid #f0f2ef;
  border-radius: 9px;
}
.today-lessons li > span {
  font-size: 12px;
  color: #a3ada5;
  font-variant-numeric: tabular-nums;
}
.today-lessons strong {
  font-size: 14px;
  font-weight: 500;
}
.today-lessons small {
  margin-left: auto;
  font-size: 10px;
}
.today-lessons .lesson-current {
  color: #2b6654;
  background: #eef5ef;
  border-color: transparent;
}
.next-lesson {
  color: #728579;
  font-size: 12px;
  padding: 12px 23px;
  border-top: 1px solid #eef1ec;
}
@media (max-width: 1100px) {
  .teaching-actions {
    gap: 10px;
  }
  .teaching-action {
    gap: 9px;
    padding: 18px 12px;
  }
  .teaching-action > svg {
    display: none;
  }
  .teaching-action strong {
    font-size: 14px;
  }
}
@media (max-width: 700px) {
  .teaching-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 10px;
    margin-bottom: 14px;
  }
  .teaching-actions.three-actions {
    grid-template-columns: 1fr;
  }
  .three-actions .teaching-action {
    min-height: 66px;
    padding: 12px 16px;
  }
  .teaching-action {
    min-height: 82px;
    padding: 14px;
  }
  .teaching-grid {
    grid-template-columns: 1fr;
    gap: 16px;
  }
  .teaching-card {
    height: 380px;
  }
  .teaching-card > header {
    padding: 20px 18px 16px;
  }
  .teaching-scroll {
    padding-left: 18px;
    padding-right: 18px;
  }
  .post-tabs {
    padding-left: 18px;
  }
  .class-connection,
  .resume-draft {
    padding: 12px;
    font-size: 12px;
  }
  .class-connection button,
  .resume-draft button {
    font-size: 11px;
  }
}
</style>
