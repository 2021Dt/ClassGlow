import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "**/*.pw.ts",
  workers: 1,
  timeout: 60000,
  outputDir: "../.qa/mobile-tests",
  reporter: [["list"], ["json", { outputFile: "../.qa/mobile-results.json" }]],
  webServer: [
    {
      command: "uv run python -m scripts.e2e_server",
      cwd: "..",
      url: "http://127.0.0.1:8019/setup/status",
      reuseExistingServer: false,
      timeout: 60000,
    },
    {
      command: "npm run dev -- --port 5179 --strictPort",
      url: "http://127.0.0.1:5179",
      reuseExistingServer: false,
      env: { KANBAN_DEV_API_TARGET: "http://127.0.0.1:8019" },
    },
  ],
  use: {
    baseURL: "http://127.0.0.1:5179",
    browserName: "chromium",
    channel: process.env.PLAYWRIGHT_CHANNEL,
    hasTouch: true,
    isMobile: true,
    screenshot: "only-on-failure",
    // 不记录包含教师会话和本机草稿的 trace；只使用独立测试账号。
    trace: "off",
  },
});
