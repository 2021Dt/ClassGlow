<script setup lang="ts">
import { School, Monitor, ArrowUpRight, ScrollText } from "@lucide/vue";
import { accountRole, accountStatus, adminDate, auditNames } from "../admin";
defineProps<{ kind: string; rows: any[] }>();
const emit = defineEmits<{
  edit: [string, any];
  license: [any];
  openClass: [string];
}>();
</script>
<template>
  <div class="admin-table-wrap">
    <table class="admin-table" :class="`table-${kind}`">
      <thead>
        <tr v-if="kind === 'users'">
          <th>姓名 / 账号</th>
          <th>身份与任教班级</th>
          <th>账号有效期</th>
          <th>高级许可</th>
          <th>状态</th>
          <th>操作</th>
        </tr>
        <tr v-else-if="kind === 'classes'">
          <th>班级名称</th>
          <th>负责人</th>
          <th>成员</th>
          <th>状态</th>
          <th>操作</th>
        </tr>
        <tr v-else-if="kind === 'devices'">
          <th>设备名称</th>
          <th>所属班级</th>
          <th>连接状态</th>
          <th>同步情况</th>
          <th>最近联系</th>
          <th>操作</th>
        </tr>
        <tr v-else>
          <th>操作</th>
          <th>操作人</th>
          <th>操作对象</th>
          <th>时间</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in rows" :key="item.id">
          <template v-if="kind === 'users'">
            <td data-label="姓名 / 账号" class="primary-cell">
              <span
                class="admin-avatar"
                :class="
                  item.systemAdmin
                    ? 'admin'
                    : item.canCreateClasses
                      ? 'headteacher'
                      : 'teacher'
                "
                >{{ item.name.slice(0, 1) }}</span
              ><button class="table-person" @click="emit('edit', 'edit', item)">
                <strong>{{ item.name }}</strong
                ><small>{{ item.login }}</small>
              </button>
            </td>
            <td data-label="身份与班级">
              <span
                class="admin-role"
                :class="{ elevated: item.systemAdmin }"
                >{{ accountRole(item) }}</span
              ><small
                class="cell-subtext class-assignment"
                :title="item.classes.map((c: any) => c.name).join('、')"
                >{{
                  item.systemAdmin
                    ? "管理全部班级"
                    : item.classes.map((c: any) => c.name).join("、") ||
                      "尚未分配班级"
                }}</small
              >
            </td>
            <td data-label="账号有效期">
              <span>{{
                item.expiresAt ? adminDate(item.expiresAt) : "长期有效"
              }}</span
              ><small v-if="item.expiresAt" class="cell-subtext"
                >北京时间</small
              >
            </td>
            <td data-label="高级许可">
              <span v-if="item.systemAdmin" class="admin-pill muted"
                >管理员权限</span
              ><template v-else
                ><span
                  class="admin-pill"
                  :class="item.premiumActive ? 'blue' : 'muted'"
                  >{{ item.premiumActive ? "高级版" : "基础版" }}</span
                ><small v-if="item.premiumUntil" class="cell-subtext"
                  >{{ adminDate(item.premiumUntil) }}
                  {{
                    item.premiumUntil > item.serverTime ? "到期" : "已过期"
                  }}</small
                ></template
              >
            </td>
            <td data-label="状态">
              <span class="admin-pill" :class="accountStatus(item).tone"
                ><i></i>{{ accountStatus(item).label }}</span
              >
            </td>
            <td data-label="操作" class="table-actions">
              <button @click="emit('edit', 'edit', item)">编辑</button
              ><button v-if="!item.systemAdmin" @click="emit('license', item)">
                许可</button
              ><button @click="emit('edit', 'password', item)">重置密码</button>
            </td>
          </template>
          <template v-else-if="kind === 'classes'">
            <td data-label="班级" class="primary-cell">
              <span class="table-object-icon"><School :size="20" /></span
              ><span
                ><strong>{{ item.name }}</strong
                ><small class="cell-subtext"
                  >{{ item.stage }} · {{ item.subjects.length }} 门学科</small
                ></span
              >
            </td>
            <td data-label="负责人">
              <strong>{{ item.owner.name }}</strong
              ><small class="cell-subtext">{{ item.owner.login }}</small>
            </td>
            <td data-label="成员">{{ item.members }} 人</td>
            <td data-label="状态">
              <span
                class="admin-pill"
                :class="item.archived ? 'muted' : 'green'"
                >{{ item.archived ? "已归档" : "使用中" }}</span
              >
            </td>
            <td data-label="操作" class="table-actions">
              <button @click="emit('edit', 'class', item)">管理</button
              ><button @click="emit('openClass', item.id)">
                进入班级<ArrowUpRight :size="13" />
              </button>
            </td>
          </template>
          <template v-else-if="kind === 'devices'">
            <td data-label="设备" class="primary-cell">
              <span class="table-object-icon"><Monitor :size="19" /></span
              ><strong>{{ item.name }}</strong>
            </td>
            <td data-label="班级">{{ item.className }}</td>
            <td data-label="连接状态">
              <span
                class="admin-pill"
                :class="
                  item.revoked ? 'muted' : item.online ? 'green' : 'amber'
                "
                ><i></i
                >{{
                  item.revoked ? "已解绑" : item.online ? "在线" : "离线"
                }}</span
              >
            </td>
            <td data-label="同步情况">
              <strong>{{
                !item.applied
                  ? "尚未应用画面"
                  : item.cached === item.applied
                    ? "显示已同步"
                    : "待应用更新"
              }}</strong
              ><small class="cell-subtext"
                >缓存 v{{ item.cached }} · 显示 v{{ item.applied }}</small
              >
            </td>
            <td data-label="最近联系">
              {{
                new Date(item.reported).getFullYear() > 1970
                  ? adminDate(item.reported, true)
                  : "尚未联系"
              }}
            </td>
            <td data-label="操作" class="table-actions">
              <button
                :disabled="item.revoked"
                class="destructive"
                @click="emit('edit', 'revoke', item)"
              >
                解绑设备
              </button>
            </td>
          </template>
          <template v-else
            ><td data-label="操作">
              <span class="admin-audit-action"
                ><ScrollText :size="15" />{{
                  auditNames[item.action] ?? item.action
                }}</span
              >
            </td>
            <td data-label="操作人">
              {{
                item.actor === "server-console" ? "服务器管理员" : item.actor
              }}
            </td>
            <td data-label="对象" class="audit-target">
              {{ item.target || "—" }}
            </td>
            <td data-label="时间">{{ adminDate(item.at, true) }}</td></template
          >
        </tr>
      </tbody>
    </table>
  </div>
</template>
