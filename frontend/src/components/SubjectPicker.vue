<script setup lang="ts">
import { ref } from "vue";
import { Plus, X } from "@lucide/vue";
import { subjectPresets } from "../subjects";
const props = defineProps<{ modelValue: string[]; stage: string }>();
const emit = defineEmits<{ "update:modelValue": [string[]] }>();
const input = ref(""),
  error = ref("");
function add(values: string[]) {
  const next = [
    ...new Set([
      ...props.modelValue,
      ...values.map((s) => s.trim()).filter(Boolean),
    ]),
  ];
  if (next.length > 20 || next.some((s) => s.length > 20)) {
    error.value = "最多 20 门学科，每门名称不超过 20 个字。";
    return;
  }
  emit("update:modelValue", next);
  input.value = "";
  error.value = "";
}
function remove(subject: string) {
  if (props.modelValue.length === 1) {
    error.value = "请至少保留一门学科。";
    return;
  }
  emit(
    "update:modelValue",
    props.modelValue.filter((s) => s !== subject),
  );
  error.value = "";
}
</script>
<template>
  <div class="subject-picker">
    <div class="section-heading">
      <strong>班级学科</strong
      ><button
        type="button"
        class="text-button"
        @click="add(subjectPresets[stage] ?? [])"
      >
        补全{{ stage }}学科
      </button>
    </div>
    <div class="subject-chips">
      <span v-for="subject in modelValue" :key="subject"
        >{{ subject
        }}<button
          type="button"
          :aria-label="`移除学科${subject}`"
          @click="remove(subject)"
        >
          <X :size="14" /></button
      ></span>
    </div>
    <div class="subject-add">
      <input
        v-model="input"
        aria-label="自定义学科"
        maxlength="100"
        placeholder="例如日语、心理健康"
        @keydown.enter.prevent="add(input.split(/[、,，\n]/))"
      /><button
        type="button"
        class="button secondary"
        :disabled="!input.trim()"
        @click="add(input.split(/[、,，\n]/))"
      >
        <Plus :size="16" />添加
      </button>
    </div>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
  </div>
</template>
<style scoped>
.subject-picker {
  display: grid;
  gap: 12px;
  margin: 16px 0;
}
.subject-picker .section-heading {
  margin: 0;
}
.subject-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.subject-chips > span {
  display: flex;
  gap: 6px;
  align-items: center;
  border-radius: 10px;
  background: #eef3fa;
  color: #334966;
  padding: 5px 8px 5px 12px;
  font-size: 13px;
}
.subject-chips button {
  display: grid;
  place-items: center;
  border: 0;
  background: transparent;
  width: 28px;
  height: 28px;
  color: inherit;
  cursor: pointer;
}
.subject-add {
  display: flex;
  gap: 8px;
}
.subject-add input {
  min-width: 0;
  flex: 1;
}
</style>
