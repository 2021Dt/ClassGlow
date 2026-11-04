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
    rolldownOptions: { input: "desktop.html" },
  },
});
