import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// 单独打包离线材质入口，不包含教师工作台、登录或联网代码。
export default defineConfig({
  base: "./",
  plugins: [vue()],
  publicDir: false,
  build: {
    outDir: "../dist/desktop-material",
    emptyOutDir: true,
    rolldownOptions: {
      input: "desktop.html",
      // 离线资源随安装包覆盖，版本号已参与材质缓存键；固定名称避免升级遗留旧文件。
      output: {
        entryFileNames: "assets/material.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: "assets/[name][extname]",
      },
    },
  },
});
