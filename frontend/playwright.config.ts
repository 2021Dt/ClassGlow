import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "**/*.pw.ts",
  workers: 1,
  timeout: 60000,
  outputDir: "../.qa/mobile-tests",
  reporter: [["list"], ["json", { outputFile: "../.qa/mobile-results.json" }]],
  use: {
    baseURL: process.env.KANBAN_TEST_URL ?? "http://127.0.0.1:5173",
    browserName: "chromium",
    channel: process.env.PLAYWRIGHT_CHANNEL,
    hasTouch: true,
    isMobile: true,
    screenshot: "only-on-failure",
    // 不记录包含教师会话和本机草稿的 trace；只使用独立测试账号。
    trace: "off",
  },
});
