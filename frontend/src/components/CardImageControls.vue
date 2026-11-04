<script setup lang="ts">
import { ref } from "vue";
import { ImagePlus, Trash2 } from "@lucide/vue";
import type { ImageFit, ImagePosition } from "../domain";
import { widgetImage } from "../canvas";

const props = withDefaults(
  defineProps<{
    image?: string;
    fit?: ImageFit;
    position?: ImagePosition;
    targetId?: string;
    large?: boolean;
    disabled?: boolean;
  }>(),
  { image: "", fit: "cover", position: "center", targetId: "" },
);
const emit = defineEmits<{
  update: [
    value: {
      targetId: string;
      image: string;
      fit: ImageFit;
      position: ImagePosition;
    },
  ];
}>();
const busy = ref(false),
  error = ref("");
function change(patch: {
  image?: string;
  fit?: ImageFit;
  position?: ImagePosition;
}) {
  emit("update", {
    targetId: props.targetId,
    image: props.image,
    fit: props.fit,
    position: props.position,
    ...patch,
  });
}
async function upload(event: Event) {
  const input = event.target as HTMLInputElement,
    file = input.files?.[0];
  if (!file) return;
  const targetId = props.targetId;
  busy.value = true;
  error.value = "";
  try {
    const image = await widgetImage(file, props.large);
    // 图片处理期间切换组件时，始终写回最初选中的卡片。
    emit("update", { targetId, image, fit: "cover", position: "center" });
  } catch (reason) {
    error.value =
      reason instanceof Error ? reason.message : "图片读取失败，请重新选择。";
  } finally {
    busy.value = false;
    input.value = "";
  }
}
</script>

<template>
  <fieldset class="card-image-controls" :disabled="disabled || busy">
    <legend>{{ large ? "大底板背景图片" : "卡片背景图片" }}</legend>
    <img
      v-if="image"
      class="card-image-thumbnail"
      :src="image"
      alt="卡片背景预览"
      :style="{ objectFit: fit, objectPosition: position }"
    />
    <label class="card-image-upload">
      <ImagePlus :size="17" />{{
        busy ? "正在处理…" : image ? "更换图片" : "上传图片"
      }}
      <input
        type="file"
        accept="image/jpeg,image/png,image/webp"
        @change="upload"
      />
    </label>
    <template v-if="image">
      <label class="field"
        >图片铺放<AppSelect
          :value="fit"
          @change="
            change({
              fit: ($event.target as HTMLSelectElement).value as ImageFit,
            })
          "
        >
          <option value="cover">铺满卡片</option>
          <option value="contain">完整显示</option>
        </AppSelect></label
      >
      <label class="field"
        >图片位置<AppSelect
          :value="position"
          @change="
            change({
              position: ($event.target as HTMLSelectElement)
                .value as ImagePosition,
            })
          "
        >
          <option value="center">居中</option>
          <option value="top">靠上</option>
          <option value="bottom">靠下</option>
          <option value="left">靠左</option>
          <option value="right">靠右</option>
        </AppSelect></label
      >
      <button type="button" class="text-button" @click="change({ image: '' })">
        <Trash2 :size="14" />移除图片
      </button>
    </template>
    <small v-else class="muted">JPG、PNG 或 WebP，最大 8 MB</small>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
  </fieldset>
</template>

<style scoped>
.card-image-controls {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
  min-width: 0;
}
.card-image-controls legend {
  font-size: 13px;
  padding-top: 12px;
}
.card-image-thumbnail {
  width: 100%;
  height: 100px;
  display: block;
  background: #f1f2f5;
  border-radius: 12px;
  margin-bottom: 10px;
}
.card-image-upload {
  display: flex;
  gap: 8px;
  align-items: center;
  justify-content: center;
  position: relative;
  padding: 11px;
  border: 1px dashed #c8ced6;
  border-radius: 12px;
  color: var(--green);
  font-size: 12px;
  cursor: pointer;
  overflow: hidden;
}
.card-image-upload input {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  cursor: pointer;
}
.card-image-upload:focus-within {
  outline: 2px solid var(--green);
  outline-offset: 3px;
}
.card-image-controls small {
  display: block;
  font-size: 11px;
  margin-top: 8px;
}
.card-image-controls .text-button {
  margin-top: 12px;
}
.card-image-controls:disabled .card-image-upload {
  opacity: 0.55;
  cursor: wait;
}
</style>
