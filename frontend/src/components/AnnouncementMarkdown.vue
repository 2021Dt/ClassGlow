<script lang="ts">
import MarkdownIt from "markdown-it";

const markdown = new MarkdownIt({ html: false, breaks: true });
const imageRule = markdown.renderer.rules.image!;

markdown.renderer.rules.link_open = (
  tokens,
  index,
  options,
  _env,
  renderer,
) => {
  tokens[index]!.attrSet("target", "_blank");
  tokens[index]!.attrSet("rel", "noopener noreferrer");
  return renderer.renderToken(tokens, index, options);
};
markdown.renderer.rules.image = (tokens, index, options, env, renderer) => {
  const token = tokens[index]!;
  const src = String(token.attrGet("src") ?? "");
  const escape = markdown.utils.escapeHtml;
  const caption = renderer.renderInlineAsText(
    token.children ?? [],
    options,
    env,
  );
  try {
    const url = new URL(src, location.origin);
    if (url.origin === location.origin || url.protocol === "data:") {
      token.attrSet("loading", "lazy");
      return imageRule(tokens, index, options, env, renderer);
    }
    // 和站点的图片加载策略一致，外部图片作为链接打开。
    return (
      '<a href="' +
      escape(src) +
      '" target="_blank" rel="noopener noreferrer">' +
      escape(caption || "查看图片") +
      "</a>"
    );
  } catch {
    return escape(caption);
  }
};
</script>

<script setup lang="ts">
import { computed } from "vue";
const props = defineProps<{ source: string }>();
const rendered = computed(() => markdown.render(props.source));
</script>

<template>
  <div class="announcement-markdown" v-html="rendered"></div>
</template>

<style scoped>
.announcement-markdown {
  min-width: 0;
  overflow-wrap: anywhere;
  line-height: 1.75;
  color: #33333a;
}
.announcement-markdown :deep(> *) {
  margin: 0 0 0.8em;
}
.announcement-markdown :deep(> :last-child) {
  margin-bottom: 0;
}
.announcement-markdown :deep(h1),
.announcement-markdown :deep(h2),
.announcement-markdown :deep(h3) {
  font-size: 1.16em;
  line-height: 1.45;
}
.announcement-markdown :deep(ul),
.announcement-markdown :deep(ol) {
  padding-left: 1.65em;
}
.announcement-markdown :deep(blockquote) {
  border-left: 3px solid #a8c6f0;
  padding-left: 12px;
  color: #555565;
}
.announcement-markdown :deep(pre),
.announcement-markdown :deep(code) {
  background: #f1f3f7;
  border-radius: 6px;
}
.announcement-markdown :deep(code) {
  padding: 0.1em 0.3em;
}
.announcement-markdown :deep(pre) {
  padding: 12px;
  white-space: pre-wrap;
}
.announcement-markdown :deep(pre code) {
  padding: 0;
}
.announcement-markdown :deep(table) {
  border-collapse: collapse;
  max-width: 100%;
}
.announcement-markdown :deep(td),
.announcement-markdown :deep(th) {
  border: 1px solid #dde2eb;
  padding: 5px 8px;
}
.announcement-markdown :deep(a) {
  color: #0068d9;
}
.announcement-markdown :deep(img) {
  max-width: 100%;
  height: auto;
}
</style>
