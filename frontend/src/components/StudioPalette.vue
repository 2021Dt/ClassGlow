<script setup lang="ts">
import "../magicui.css";
import { onMounted, onBeforeUnmount, ref, watch } from "vue";
import { createElement } from "react";
import { createRoot, type Root } from "react-dom/client";
import { StudioPalette } from "../react/StudioPalette";
import type { WidgetKind } from "../domain";
const props = defineProps<{ disabled: boolean }>();
const emit = defineEmits<{ add: [WidgetKind] }>();
const host = ref<HTMLElement>();
let root: Root;
const onAdd = (kind: WidgetKind) => emit("add", kind);
function render() {
  root?.render(
    createElement(StudioPalette, { disabled: props.disabled, onAdd }),
  );
}
onMounted(() => {
  root = createRoot(host.value!);
  render();
});
watch(() => props.disabled, render);
onBeforeUnmount(() => root?.unmount());
</script>
<template><div ref="host" class="studio-palette-root" /></template>
