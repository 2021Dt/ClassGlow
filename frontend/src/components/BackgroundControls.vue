<script setup lang="ts">
import { ref } from "vue";
import type { Layout } from "../domain";
const props = defineProps<{ modelValue: Layout; disabled?: boolean }>();
const emit = defineEmits<{ "update:modelValue": [Layout] }>();
const busy = ref(false),
  error = ref("");
function change(patch: Partial<Layout>) {
  emit("update:modelValue", { ...props.modelValue, ...patch });
}
async function upload(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  error.value = "";
  busy.value = true;
  let bitmap: ImageBitmap | undefined;
  try {
    if (
      !["image/jpeg", "image/png", "image/webp"].includes(file.type) ||
      file.size > 8 * 1024 * 1024
    )
      throw new Error("请选择 8 MB 以内的 JPG、PNG 或 WebP 图片。");
    bitmap = await createImageBitmap(file);
    const canvas = document.createElement("canvas"),
      context = canvas.getContext("2d")!;
    // 统一居中裁切为 16:9，并转成小体积 JPEG；不保存原始照片的 EXIF 信息。
    const cropWidth = Math.min(bitmap.width, (bitmap.height * 16) / 9),
      cropHeight = (cropWidth * 9) / 16;
    let result = "";
    for (const width of [1920, 1280]) {
      canvas.width = width;
      canvas.height = (width * 9) / 16;
      context.fillStyle = props.modelValue.backgroundColor ?? "#E7E8EB";
      context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(
        bitmap,
        (bitmap.width - cropWidth) / 2,
        (bitmap.height - cropHeight) / 2,
        cropWidth,
        cropHeight,
        0,
        0,
        canvas.width,
        canvas.height,
      );
      for (const quality of [0.85, 0.7, 0.55, 0.4]) {
        result = canvas.toDataURL("image/jpeg", quality);
        if (result.length <= 524311) break;
      }
      if (result.length <= 524311) break;
    }
    if (result.length > 524311)
      throw new Error("图片细节过多，请换一张更简洁的背景。");
    change({ backgroundMode: "image", backgroundImage: result });
  } catch (reason) {
    error.value =
      reason instanceof Error ? reason.message : "图片读取失败，请重新选择。";
  } finally {
    bitmap?.close();
    busy.value = false;
    input.value = "";
  }
}
</script>
<template>
  <fieldset class="background-controls" :disabled="disabled || busy">
    <legend>背景与通透度</legend>
    <label class="field"
      >背景类型<select
        :value="modelValue.backgroundMode ?? 'mica'"
        @change="
          change({
            backgroundMode: ($event.target as HTMLSelectElement)
              .value as Layout['backgroundMode'],
          })
        "
      >
        <option value="mica">云母 · 跟随主题</option>
        <option value="color">自定义纯色</option>
        <option value="image">自定义图片</option>
      </select></label
    >
    <label v-if="modelValue.backgroundMode === 'color'" class="field"
      >背景底色<input
        type="color"
        :value="modelValue.backgroundColor ?? '#E7E8EB'"
        @input="
          change({ backgroundColor: ($event.target as HTMLInputElement).value })
        "
    /></label>
    <template v-if="modelValue.backgroundMode === 'image'">
      <label class="field"
        >上传背景图片<input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          @change="upload"
      /></label>
      <img
        v-if="modelValue.backgroundImage"
        :src="modelValue.backgroundImage"
        class="background-thumbnail"
        alt="待发布的自定义背景"
      />
      <button
        v-if="modelValue.backgroundImage"
        class="text-button"
        type="button"
        @click="change({ backgroundImage: '', backgroundMode: 'mica' })"
      >
        移除图片，恢复云母
      </button>
      <p class="muted small">
        JPG / PNG / WebP，最大 8 MB；自动居中裁切为 16:9。
      </p>
    </template>
    <label class="field"
      >卡片不透明度 · {{ modelValue.surfaceOpacity ?? 38 }}%<input
        type="range"
        min="20"
        max="95"
        step="1"
        :value="modelValue.surfaceOpacity ?? 38"
        :disabled="modelValue.material === 'solid'"
        @input="
          change({
            surfaceOpacity: Number(($event.target as HTMLInputElement).value),
          })
        "
    /></label>
    <p class="muted small">
      数值越小越通透，正文颜色保持不变。纯色材质始终不透明。
    </p>
    <p v-if="busy" class="muted small">正在处理图片…</p>
    <p v-if="error" class="notice danger" role="alert">{{ error }}</p>
  </fieldset>
</template>
<style scoped>
.background-controls {
  min-width: 0;
  padding: 18px 0;
  margin-top: 20px;
  border: 0;
  border-top: 1px solid var(--border);
}
.background-controls legend {
  font-size: 13px;
  padding-top: 14px;
}
.background-controls .field {
  margin-top: 12px;
}
.background-controls input[type="file"] {
  max-width: 100%;
  min-width: 0;
  font-size: 12px;
  padding: 8px 0;
}
.background-controls input[type="color"] {
  width: 100%;
  height: 42px;
  padding: 4px;
}
.background-controls input[type="range"] {
  width: 100%;
  accent-color: #426b5c;
}
.background-thumbnail {
  width: 100%;
  aspect-ratio: 16/9;
  object-fit: cover;
  border-radius: 8px;
  margin-top: 10px;
}
</style>
