<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch } from "vue";
import { createElement } from "react";
import { createRoot, type Root } from "react-dom/client";
import LiquidGlass from "liquid-glass-react";

const props = defineProps<{
  width: number;
  height: number;
  radius: number;
  scale: number;
}>();
const host = ref<HTMLElement>();
let root: Root;
function render() {
  if (!root) return;
  const width = props.width * props.scale,
    height = props.height * props.scale;
  // 原库按屏幕像素测量尺寸，因此抵消画布缩放后再交给原组件渲染。
  // 原库只在挂载/窗口缩放时测量；卡片改变尺寸时用 key 重建，避免旧的边框尺寸。
  root.render(
    createElement(LiquidGlass, {
      key: `${width}:${height}`,
      padding: "0",
      cornerRadius: props.radius * props.scale,
      displacementScale: 64 * props.scale,
      blurAmount: 0.0625,
      saturation: 140,
      aberrationIntensity: 1.5,
      elasticity: 0,
      mode: "shader",
      mouseContainer: { current: host.value?.parentElement ?? null },
      style: { position: "absolute", top: "50%", left: "50%", width, height },
      children: createElement("div", { style: { width, height } }),
    }),
  );
}
onMounted(() => {
  root = createRoot(host.value!);
  render();
});
watch(() => [props.width, props.height, props.radius, props.scale], render, {
  flush: "post",
});
onBeforeUnmount(() => root?.unmount());
</script>
<template>
  <div
    ref="host"
    class="liquid-glass-layer"
    aria-hidden="true"
    :style="{
      width: `${width * scale}px`,
      height: `${height * scale}px`,
      transform: `scale(${1 / scale})`,
    }"
  />
</template>
