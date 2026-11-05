<script setup lang="ts">
import { onMounted, ref } from "vue";
import { api } from "../api";
import { downloadJson } from "../downloadJson";
const props = defineProps<{ overview: unknown }>();
type Metrics = {
  since: number;
  requests: number;
  errors: number;
  routes: {
    method: string;
    route: string;
    count: number;
    errors: number;
    p95Ms: number;
  }[];
};
const value = ref<Metrics>(),
  error = ref("");
async function load() {
  error.value = "";
  try {
    value.value = await api<Metrics>("/system/metrics");
  } catch (e) {
    error.value = (e as Error).message;
  }
}
function download() {
  downloadJson("ClassGlow-server-diagnostics.json", {
    generatedAt: new Date().toISOString(),
    overview: props.overview,
    requests: value.value,
  });
}
onMounted(load);
</script>
<template>
  <section class="admin-card">
    <header class="admin-card-heading">
      <h2>服务运行诊断</h2>
      <button class="button secondary" @click="load">刷新诊断</button>
    </header>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <template v-if="value">
      <p>
        本次启动已处理 {{ value.requests }} 次请求，服务错误
        {{ value.errors }} 次。
      </p>
      <details>
        <summary>查看接口耗时</summary>
        <p class="muted small">
          按各接口最近 128 次请求的第 95 百分位耗时排序，统计随服务重启清空。
        </p>
        <div style="overflow-x: auto">
          <table>
            <thead>
              <tr>
                <th>接口</th>
                <th>请求数</th>
                <th>服务错误</th>
                <th>耗时</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="item in value.routes.slice(0, 10)"
                :key="item.method + item.route"
              >
                <td>{{ item.method }} {{ item.route }}</td>
                <td>{{ item.count }}</td>
                <td>{{ item.errors }}</td>
                <td>{{ item.p95Ms }} ms</td>
              </tr>
            </tbody>
          </table>
        </div>
      </details>
      <button class="button secondary" @click="download">导出运行诊断</button>
    </template>
  </section>
</template>
