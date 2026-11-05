<script setup lang="ts">
import { ref, onMounted } from "vue";
import { CircleHelp, ChevronDown } from "@lucide/vue";
import { session } from "../api";
const props = defineProps<{
  name: string;
  title: string;
  steps: { title: string; text: string }[];
}>();
const open = ref(false);
const key = () => `classglow-help-v1-${session.userId}-${props.name}`;
onMounted(() => {
  try {
    open.value = !localStorage.getItem(key());
  } catch {
    open.value = true;
  }
});
function toggle(event: Event) {
  open.value = (event.target as HTMLDetailsElement).open;
  if (!open.value)
    try {
      localStorage.setItem(key(), "seen");
    } catch {
      /* 说明始终可重看。 */
    }
}
</script>
<template>
  <details class="task-guide" :open="open" @toggle="toggle">
    <summary>
      <CircleHelp :size="18" /><span>{{ title }}</span
      ><small>{{ open ? "收起" : "查看步骤" }}</small
      ><ChevronDown :size="16" />
    </summary>
    <ol>
      <li v-for="(step, index) in steps" :key="step.title">
        <span class="step-number">{{ index + 1 }}</span>
        <div>
          <strong>{{ step.title }}</strong>
          <p>{{ step.text }}</p>
        </div>
      </li>
    </ol>
    <button type="button" class="guide-done" @click="open = false">
      知道了，收起说明
    </button>
  </details>
</template>
<style scoped>
.task-guide {
  flex-shrink: 0;
  background: #f2f7fc;
  border: 1px solid #dce7f2;
  border-radius: 14px;
  margin: 12px 0;
  color: #31506b;
  min-width: 0;
}
summary {
  display: flex;
  align-items: center;
  gap: 9px;
  cursor: pointer;
  padding: 12px 16px;
  min-height: 44px;
  list-style: none;
}
summary::-webkit-details-marker {
  display: none;
}
summary span {
  flex: 1;
  font-size: 14px;
  font-weight: 600;
}
summary small {
  font-size: 12px;
  white-space: nowrap;
}
ol {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 230px), 1fr));
  gap: 18px;
  padding: 6px 16px 12px;
  margin: 0;
  list-style: none;
}
li {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  min-width: 0;
}
.step-number {
  flex: none;
  display: grid;
  place-items: center;
  width: 25px;
  height: 25px;
  background: #dceaf7;
  border-radius: 50%;
  font-size: 12px;
}
strong {
  font-size: 13px;
}
p {
  margin: 6px 0 0;
  font-size: 12px;
  line-height: 1.8;
  overflow-wrap: anywhere;
}
.guide-done {
  display: block;
  min-height: 44px;
  padding: 8px 16px;
  margin: 0 12px 8px auto;
  background: transparent;
  color: #2861a3;
  border-radius: 8px;
}
@media (max-width: 600px) {
  ol {
    grid-template-columns: 1fr;
    gap: 14px;
  }
  summary {
    padding: 10px 12px;
  }
}
</style>
