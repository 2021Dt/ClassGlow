<script setup lang="ts">
import { ref } from "vue";
import {
  Database,
  Download,
  Trash2,
  X,
  ShieldCheck,
  ChevronRight,
} from "@lucide/vue";
import { api } from "../api";
import { adminDate, fileSize } from "../admin";
type Backup = { name: string; source: string; at: number; bytes: number };
defineProps<{ rows: Backup[]; supported: boolean; busy: boolean }>();
const emit = defineEmits<{
  create: [];
  download: [string];
  restore: [Backup];
  changed: [];
}>();
const dialog = ref<HTMLDialogElement>(),
  selected = ref<Backup>(),
  details = ref<{
    counts: { label: string; count: number }[];
    schemaVersion: string;
  }>();
const error = ref(""),
  working = ref(false),
  deleting = ref(false);
async function open(item: Backup, remove = false) {
  selected.value = item;
  deleting.value = remove;
  details.value = undefined;
  error.value = "";
  dialog.value?.showModal();
  if (remove) return;
  working.value = true;
  try {
    details.value = await api(
      `/system/backups/${encodeURIComponent(item.name)}`,
    );
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    working.value = false;
  }
}
async function remove() {
  if (!selected.value || working.value) return;
  working.value = true;
  error.value = "";
  try {
    await api(
      `/system/backups/${encodeURIComponent(selected.value.name)}`,
      "DELETE",
    );
    dialog.value?.close();
    emit("changed");
  } catch (e) {
    error.value = (e as Error).message;
  } finally {
    working.value = false;
  }
}
</script>

<template>
  <section class="admin-card backup-list">
    <div class="admin-backup-intro">
      <span class="metric-icon blue"><Database :size="22" /></span>
      <div>
        <h2>数据库备份</h2>
        <p>保存备份时刻的全站数据，可查看内容数量、下载或恢复。</p>
      </div>
      <span class="admin-pill muted">{{ rows.length }} 份备份</span>
    </div>
    <div class="backup-scope">
      <span
        ><strong>包含</strong
        >账号与权限、班级与课表、作业与提醒、草稿、许可、看板设计、设备与操作记录。</span
      ><span
        ><strong>不包含</strong>服务器配置、证书、安装包和 Windows
        本机缓存。</span
      >
    </div>
    <p v-if="!supported" class="admin-feedback info">
      当前使用 PostgreSQL，请通过服务器菜单创建与恢复备份。
    </p>
    <div v-if="!rows.length" class="admin-empty">
      <Database :size="30" />
      <h3>还没有备份</h3>
      <button
        class="button secondary"
        :disabled="busy || !supported"
        @click="emit('create')"
      >
        创建第一份备份
      </button>
    </div>
    <div v-else class="admin-table-wrap">
      <table class="admin-table">
        <thead>
          <tr>
            <th>备份</th>
            <th>创建时间</th>
            <th>大小</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in rows" :key="item.name">
            <td data-label="备份">
              <button class="backup-name" @click="open(item)">
                <span
                  ><strong>{{ item.source }}</strong
                  ><small>{{ item.name }}</small></span
                ><ChevronRight :size="16" />
              </button>
            </td>
            <td data-label="创建时间">{{ adminDate(item.at, true) }}</td>
            <td data-label="大小">{{ fileSize(item.bytes) }}</td>
            <td data-label="操作" class="table-actions">
              <button :disabled="busy" @click="open(item)">详情</button
              ><button :disabled="busy" @click="emit('download', item.name)">
                <Download :size="14" />下载</button
              ><button
                :disabled="busy || !supported"
                @click="emit('restore', item)"
              >
                恢复</button
              ><button
                class="destructive"
                :disabled="busy"
                @click="open(item, true)"
              >
                <Trash2 :size="14" />删除
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="admin-tip">
      <ShieldCheck :size="17" />
      <p>
        备份含账号认证数据与教学内容，请妥善保管。恢复会替换全站数据，并要求重新登录和绑定设备；系统会先保留恢复前副本。
      </p>
    </div>
    <dialog
      ref="dialog"
      class="backup-dialog"
      @cancel="working && $event.preventDefault()"
    >
      <header>
        <div>
          <span class="admin-mini-label">{{ selected?.source }}</span>
          <h2>{{ deleting ? "删除这份备份？" : "备份内容" }}</h2>
        </div>
        <button
          class="icon-button"
          aria-label="关闭备份详情"
          :disabled="working"
          @click="dialog?.close()"
        >
          <X :size="20" />
        </button>
      </header>
      <p class="backup-filename">{{ selected?.name }}</p>
      <p class="muted">
        {{ adminDate(selected?.at || 0, true) }} ·
        {{ fileSize(selected?.bytes || 0) }}
      </p>
      <p v-if="error" role="alert" class="notice danger">{{ error }}</p>
      <template v-if="deleting"
        ><p>
          这份备份文件将永久删除，无法再用它恢复数据。正在使用的班级、账号和教学内容不受影响。
        </p>
        <footer>
          <button
            class="button secondary"
            :disabled="working"
            @click="dialog?.close()"
          >
            取消</button
          ><button
            class="button primary backup-delete"
            :disabled="working"
            @click="remove"
          >
            {{ working ? "正在删除…" : "确认删除备份" }}
          </button>
        </footer></template
      >
      <template v-else
        ><p v-if="working" role="status">正在读取备份内容…</p>
        <dl v-if="details" class="backup-counts">
          <div v-for="item in details.counts" :key="item.label">
            <dt>{{ item.label }}</dt>
            <dd>{{ item.count }}</dd>
          </div>
        </dl>
        <p class="muted small">
          数量来自此备份保存时的数据。课表、背景图和布局包含在班级记录内；旧备份可能不包含后来新增的数据类型。
        </p>
        <details v-if="details">
          <summary>技术信息</summary>
          <p class="muted small">数据库结构版本：{{ details.schemaVersion }}</p>
        </details>
        <footer>
          <button class="button secondary" @click="dialog?.close()">关闭</button
          ><button
            class="button primary"
            :disabled="busy || working"
            @click="selected && emit('download', selected.name)"
          >
            <Download :size="16" />下载备份
          </button>
        </footer></template
      >
    </dialog>
  </section>
</template>

<style scoped>
.backup-scope {
  display: grid;
  gap: 7px;
  padding: 16px 24px;
  background: #f8faff;
  font-size: 12px;
  color: #71809a;
  border-block: 1px solid #e8edf6;
  line-height: 1.8;
}
.backup-scope strong {
  display: inline-block;
  min-width: 50px;
  color: #506583;
}
.backup-name {
  display: flex;
  align-items: center;
  gap: 18px;
  text-align: left;
  border: 0;
  background: none;
  padding: 0;
  color: #637592;
  cursor: pointer;
}
.backup-name span {
  display: grid;
  gap: 6px;
}
.backup-name strong {
  font-weight: 600;
  color: #334560;
}
.backup-name small {
  font-size: 10px;
  overflow-wrap: anywhere;
}
.backup-dialog {
  width: min(560px, calc(100vw - 32px));
  border: 1px solid #e5eaf2;
  border-radius: 20px;
  padding: 26px;
  box-shadow: 0 24px 80px #15233b30;
  color: #334560;
}
.backup-dialog::backdrop {
  background: #15233b45;
  backdrop-filter: blur(3px);
}
.backup-dialog header,
.backup-dialog footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}
.backup-dialog h2 {
  margin: 6px 0 0;
  font-size: 22px;
}
.backup-dialog footer {
  justify-content: flex-end;
  margin-top: 24px;
}
.backup-filename {
  overflow-wrap: anywhere;
  font-size: 12px;
  margin: 22px 0 8px;
}
.backup-counts {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin: 22px 0;
}
.backup-counts div {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 13px;
  border-radius: 10px;
  background: #f5f7fb;
  font-size: 13px;
}
.backup-counts dd {
  margin: 0;
  font-weight: 650;
  color: #2d5fab;
}
.backup-delete {
  background: #c44848;
  border-color: #c44848;
}
@media (max-width: 500px) {
  .backup-counts {
    grid-template-columns: 1fr;
  }
  .backup-dialog {
    padding: 20px;
  }
}
</style>
