<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import { ArrowRight, X } from "@lucide/vue";
const props = defineProps<{ canManage: boolean; userId: string }>();
const emit = defineEmits<{ close: [] }>();
const dialog = ref<HTMLDialogElement>();
const index = ref(0);
const box = ref({ left: 0, top: 0, width: 0, height: 0 });
const panel = ref({ left: 16, top: 100 });
const steps = computed(() => [
  {
    target: "class-switch",
    title: "先选你要操作的班级",
    text: "点击这里切换班级。接下来发布的内容会默认发到这个班。",
  },
  {
    target: "publish",
    title: "从这里发作业",
    text: "选择科目、填写内容，点击发布。展示时间已经填好，需要时再调整。",
  },
  {
    target: "records",
    title: "已发的内容在这里",
    text: "查看发布记录，也可以修改、撤回。还没写完的内容保存在草稿箱。",
  },
  ...(props.canManage
    ? [
        {
          target: "devices",
          title: "连接教室电脑",
          text: "先在教室电脑安装客户端，再把客户端生成的绑定码填到这里。",
        },
        {
          target: "design",
          title: "打开你的看板画布",
          text: "画布会在独立页面打开。添加组件、拖动排版，发布后同步到教室。自定义设计需要高级许可。",
        },
      ]
    : []),
]);
const step = computed(() => steps.value[index.value]!);
async function position() {
  await nextTick();
  const target = Array.from(
    document.querySelectorAll<HTMLElement>(
      `[data-tour="${step.value.target}"]`,
    ),
  ).find((el) => el.getClientRects().length);
  target?.scrollIntoView({ block: "center", behavior: "instant" });
  const rect = target?.getBoundingClientRect();
  box.value = rect
    ? {
        left: rect.left - 6,
        top: rect.top - 6,
        width: rect.width + 12,
        height: rect.height + 12,
      }
    : { left: 0, top: 0, width: 0, height: 0 };
  const width = Math.min(352, innerWidth - 32),
    height = dialog.value?.offsetHeight ?? 230;
  const desired = rect ? rect.bottom + 20 : (innerHeight - height) / 2;
  panel.value = {
    left: Math.max(
      16,
      Math.min(rect?.left ?? (innerWidth - width) / 2, innerWidth - width - 16),
    ),
    top: Math.max(16, Math.min(desired, innerHeight - height - 16)),
  };
}
function finish() {
  // 仅记录当前账号在当前浏览器的引导状态，跳过后不反复打断；帮助入口可随时重看。
  try {
    localStorage.setItem(`classglow-guide-v1-${props.userId}`, "seen");
  } catch {
    /* 隐私模式下仍允许正常关闭。 */
  }
  dialog.value?.close();
  emit("close");
}
watch(index, position);
onMounted(async () => {
  dialog.value?.showModal();
  await position();
  window.addEventListener("resize", position);
});
onUnmounted(() => {
  window.removeEventListener("resize", position);
  dialog.value?.close();
});
</script>
<template>
  <dialog
    ref="dialog"
    class="first-guide"
    :style="{ left: panel.left + 'px', top: panel.top + 'px' }"
    aria-labelledby="guide-title"
    @cancel.prevent="finish"
  >
    <div
      class="guide-focus"
      aria-hidden="true"
      :style="{
        left: box.left + 'px',
        top: box.top + 'px',
        width: box.width + 'px',
        height: box.height + 'px',
      }"
    ></div>
    <div class="guide-content">
      <header>
        <span>使用引导 · {{ index + 1 }} / {{ steps.length }}</span
        ><button aria-label="跳过引导" @click="finish"><X :size="17" /></button>
      </header>
      <h2 id="guide-title">{{ step.title }}</h2>
      <p>{{ step.text }}</p>
      <footer>
        <button class="guide-skip" @click="finish">暂时跳过</button>
        <div>
          <button v-if="index > 0" class="guide-back" @click="index--">
            上一步</button
          ><button
            class="guide-next"
            @click="index === steps.length - 1 ? finish() : index++"
          >
            {{ index === steps.length - 1 ? "开始使用" : "下一步"
            }}<ArrowRight :size="15" />
          </button>
        </div>
      </footer>
    </div>
  </dialog>
</template>
<style scoped>
.first-guide {
  position: fixed;
  margin: 0;
  padding: 0;
  border: 0;
  width: min(352px, calc(100vw - 32px));
  overflow: visible;
  background: transparent;
  color: #253e32;
}
.first-guide::backdrop {
  background: transparent;
  backdrop-filter: none;
}
.guide-focus {
  position: fixed;
  border-radius: 14px;
  box-shadow: 0 0 0 9999px #101b22a6;
  border: 2px solid #ffffffdc;
  pointer-events: none;
}
.guide-content {
  position: relative;
  z-index: 1;
  background: #fff;
  border-radius: 18px;
  box-shadow: 0 20px 70px #142c3340;
  padding: 22px;
}
.guide-content header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 11px;
  color: #8c9790;
}
.guide-content header button {
  background: none;
  color: #8c9790;
  padding: 3px;
  display: grid;
  place-items: center;
}
.guide-content h2 {
  font-size: 18px;
  margin: 15px 0 10px;
}
.guide-content p {
  font-size: 13px;
  line-height: 1.9;
  color: #708076;
}
.guide-content footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 23px;
}
.guide-content footer > div {
  display: flex;
  align-items: center;
  gap: 10px;
}
.guide-skip,
.guide-back {
  background: none;
  color: #8b968f;
  font-size: 12px;
  padding: 5px 0;
}
.guide-next {
  display: flex;
  align-items: center;
  gap: 6px;
  background: #2b6654;
  color: white;
  border-radius: 22px;
  padding: 10px 16px;
  font-size: 12px;
}
</style>
