import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => ({
  plugins: [vue(), tailwindcss()],
  build: {
    rolldownOptions: {
      // 本站只在浏览器执行；保留官方组件里的 RSC 标记，忽略其打包提示。
      onwarn(warning, warn) {
        if (
          warning.code === "MODULE_LEVEL_DIRECTIVE" &&
          warning.message.includes('"use client"')
        )
          return;
        warn(warning);
      },
      output: {
        codeSplitting: {
          groups: [
            {
              name: "react-ui",
              test: /node_modules[\\/](react|react-dom|scheduler|liquid-glass-react|motion|framer-motion|motion-dom|motion-utils)[\\/]/,
            },
          ],
        },
      },
    },
  },
  server: {
    proxy: {
      "/api": {
        target:
          loadEnv(mode, ".", "KANBAN_").KANBAN_DEV_API_TARGET ||
          "http://127.0.0.1:8000",
        changeOrigin: false,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
}));
