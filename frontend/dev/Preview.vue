<script setup lang="ts">
import { computed, ref } from "vue";
import BoardPreview from "../src/components/BoardPreview.vue";
import BackgroundControls from "../src/components/BackgroundControls.vue";
import LayoutEditor from "../src/components/LayoutEditor.vue";
import type { Classroom, Content } from "../src/domain";
import snapshot from "./board-snapshot.json";
const classroom = ref(structuredClone(snapshot.classroom) as Classroom);
const page = ref(1),
  pages = ref(1),
  stress = ref(false),
  native = ref(false),
  nativeSample = ref("custom"),
  nativeLast = ref(false);
const contents = computed(
  () =>
    snapshot.contents.map((c, i) => ({
      ...c,
      body:
        stress.value && i === 0
          ? Array.from({ length: 100 }, (_, index) => `第${index + 1}行`).join(
              "\n",
            )
          : c.body,
    })) as Content[],
);
const nativeImage = computed(() =>
  stress.value
    ? `/review/windows-overflow-${nativeLast.value ? "last" : "first"}.png`
    : nativeSample.value === "layout"
      ? "/review/windows-layout.png"
      : `/review/windows-${nativeSample.value === "custom" ? "custom-" : ""}${classroom.value.layout.material === "solid" ? (classroom.value.layout.theme === "mica_dark" ? "dark-solid" : "solid") : classroom.value.layout.theme === "mica_dark" ? "dark" : "light"}.png`,
);
</script>
<template>
  <main class="material-review">
    <header class="review-heading">
      <div>
        <p class="eyebrow">CLASSGLOW · 材质与布局对照</p>
        <h1>一张大卡片，排出班级自己的布局。</h1>
        <p>
          示例数据 · 1920 × 1080 固定画布 · Windows 图片由正式 WPF 渲染器生成
        </p>
      </div>
    </header>
    <div class="review-controls">
      <label
        ><input type="checkbox" v-model="native" />Windows
        实际渲染（固定样例）</label
      >
      <label v-if="native && !stress"
        >Windows 样例<select v-model="nativeSample">
          <option value="custom">图片背景 · 作业优先</option>
          <option value="mica">默认云母 · 作业优先</option>
          <option value="layout">图片背景 · 课表在左</option>
        </select></label
      >
      <label
        >主题<select
          v-model="classroom.layout.theme"
          :disabled="native && (stress || nativeSample === 'layout')"
        >
          <option value="mica_light">浅灰云母</option>
          <option value="mica_dark">深灰云母</option>
        </select></label
      >
      <label
        >材质<select
          v-model="classroom.layout.material"
          :disabled="native && (stress || nativeSample === 'layout')"
        >
          <option value="subtle">通透</option>
          <option value="solid">纯色</option>
        </select></label
      >
      <label
        ><input type="checkbox" v-model="stress" @change="page = 1" />100
        行边界样例</label
      >
      <template v-if="!native"
        ><button class="button secondary" :disabled="page <= 1" @click="page--">
          上一页</button
        ><span>{{ page }} / {{ pages }}</span
        ><button
          class="button secondary"
          :disabled="page >= pages"
          @click="page++"
        >
          下一页
        </button></template
      >
      <button
        v-else-if="stress"
        class="button secondary"
        @click="nativeLast = !nativeLast"
      >
        {{ nativeLast ? "查看第一页" : "查看最后一页" }}
      </button>
    </div>
    <details v-if="!native" class="review-custom">
      <summary>自定义背景图片、底色与透明度</summary>
      <BackgroundControls v-model="classroom.layout" />
    </details>
    <img
      v-if="native"
      class="native-frame"
      :src="nativeImage"
      alt="Windows 正式渲染器生成的完整画面"
    />
    <BoardPreview
      v-else
      :classroom="classroom"
      :layout="classroom.layout"
      :contents="contents"
      :now="Date.parse(snapshot.previewClock)"
      :page="page"
      demo
      @pages="pages = $event"
    />
    <p class="review-caption" v-if="native">
      固定样例由正式 Windows
      渲染器生成，不随网页编辑即时变化。取消勾选可编辑并查看实时预览。
    </p>
    <p class="review-caption">
      固定画布与卡片边界，长内容按页完整展示。Windows
      使用合成壁纸；课件前台不会被玻璃层覆盖。
    </p>
    <LayoutEditor v-if="!native" v-model="classroom.layout" :disabled="false" />
  </main>
</template>
<style>
.material-review {
  max-width: 1540px;
  margin: 0 auto;
  padding: 26px 32px;
}
.review-heading h1 {
  margin: 6px 0 8px;
  font-size: 28px;
}
.review-heading p:last-child,
.review-caption {
  font-size: 12px;
  color: #657c74;
}
.review-controls {
  display: flex;
  gap: 18px;
  flex-wrap: wrap;
  align-items: center;
  margin: 20px 0;
  font-size: 12px;
}
.review-controls label {
  display: flex;
  align-items: center;
  gap: 6px;
}
.review-controls select {
  width: auto;
  padding: 7px;
}
.review-controls input {
  width: 16px;
  min-height: 16px;
}
.native-frame {
  display: block;
  width: 100%;
  aspect-ratio: 16/9;
  border-radius: 16px;
}
.review-caption {
  margin-top: 14px;
}
.review-custom {
  margin: 0 0 16px;
  max-width: 360px;
}
.review-custom summary {
  cursor: pointer;
  font-size: 13px;
}
@media (max-width: 650px) {
  .material-review {
    padding: 20px 12px;
  }
  .review-controls {
    gap: 10px;
  }
}
</style>
