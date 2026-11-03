import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig(({ mode }) => ({
  plugins: [vue()],
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
