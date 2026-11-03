import { test, expect, type Page } from "@playwright/test";

async function noOverflow(page: Page) {
  const offenders = await page.evaluate(
    (width) =>
      [...document.querySelectorAll("body *")]
        .filter((n) => {
          const b = n.getBoundingClientRect();
          return b.width > 0 && (b.right > width + 1 || b.left < -1);
        })
        .slice(0, 12)
        .map((n) => ({
          tag: n.tagName,
          class: n.className,
          width: n.getBoundingClientRect().width,
        })),
    page.viewportSize()!.width,
  );
  // 移动浏览器可能扩大布局视口来容纳溢出，不能用 innerWidth 自己与自己比较。
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
    JSON.stringify(offenders),
  ).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
  const dialog = page.getByRole("dialog");
  if (await dialog.count()) {
    expect(
      await dialog.evaluateAll((nodes) =>
        nodes
          .filter((n) => n.getBoundingClientRect().width > 0)
          .every((n) => n.scrollWidth <= n.clientWidth + 1),
      ),
    ).toBeTruthy();
  }
}

for (const width of [320, 360, 390, 768]) {
  test(`${width}px 触摸视口的登录、发布、草稿、课表和控制入口`, async ({
    page,
    context,
    request,
  }) => {
    await page.setViewportSize({ width, height: 844 });
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(e.message));
    // 每个视口建独立账号，确保没有依赖浏览器缓存或演示数据。
    await page.goto("/");
    await page.getByRole("button", { name: "还没有账号？注册" }).click();
    await page.getByLabel("老师姓名").fill("移动端回归测试");
    await page
      .getByLabel("账号", { exact: true })
      .fill(`mobile_${width}_${Date.now()}`);
    await page.getByLabel("口令", { exact: true }).fill("MobileTestOnly!123");
    await noOverflow(page);
    await page.getByRole("button", { name: "创建账号并进入" }).click();
    await page.getByRole("button", { name: "创建第一个班级" }).click();
    await page.getByLabel("班级名称").fill("移动端检查班（非真实数据）");
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "创建班级", exact: true })
      .click();
    await page
      .getByRole("heading", { name: "班级概览", exact: true })
      .waitFor();
    await noOverflow(page);
    const token = await page.evaluate(() =>
      sessionStorage.getItem("kanban-session"),
    );
    const headers = { Authorization: `Bearer ${token}` };
    await page.getByRole("button", { name: "发布作业", exact: true }).click();
    await page.getByLabel("作业内容").fill("手机发布：最后输入的完整正文。");
    await noOverflow(page);
    await page
      .getByRole("button", { name: "发布到 1 个班", exact: true })
      .click();
    await page.getByRole("dialog").waitFor({ state: "hidden" });
    const state = await (
      await request.get("/api/bootstrap", { headers })
    ).json();
    expect(state.contents[0].body).toBe("手机发布：最后输入的完整正文。");
    await page.getByRole("button", { name: "发布作业", exact: true }).click();
    await context.setOffline(true);
    await page
      .getByLabel("作业内容")
      .fill("手机离线输入，恢复网络仅保存草稿。");
    await page.waitForTimeout(2200);
    await context.setOffline(false);
    await page.getByText(/草稿已同步/).waitFor();
    await page.getByRole("button", { name: "保存草稿并关闭" }).click();
    await page.getByRole("dialog").waitFor({ state: "hidden" });
    const restored = await (
      await request.get("/api/bootstrap", { headers })
    ).json();
    expect(restored.contents).toHaveLength(1);
    expect(restored.drafts).toHaveLength(1);
    async function navigate(name: string) {
      if (width > 650) {
        await page
          .getByRole("navigation", { name: "主要导航" })
          .getByRole("button", { name, exact: true })
          .click();
      } else {
        await page.getByRole("button", { name: "我的", exact: true }).click();
        await page
          .getByRole("dialog")
          .getByRole("button", { name, exact: true })
          .click();
      }
    }
    await navigate("私人草稿");
    await page
      .getByRole("heading", { name: "私人草稿", exact: true })
      .waitFor();
    await noOverflow(page);
    await navigate("班级设置");
    await page
      .getByRole("heading", { name: "班级设置", exact: true })
      .waitFor();
    const table = page
      .locator("section.panel")
      .filter({
        has: page.getByRole("heading", { name: "每周课表", exact: true }),
      });
    await table.getByRole("button", { name: "增加一节", exact: true }).click();
    await table.getByLabel("第 1 节开始").fill("08:00");
    await table.getByLabel("第 1 节结束").fill("08:40");
    await table.getByLabel("第 1 节科目").selectOption("数学");
    await noOverflow(page);
    // iOS 小于 16px 的输入会放大页面，时间和科目输入也必须保持可触摸尺寸。
    if (width <= 650) {
      expect(
        await table
          .getByLabel("第 1 节开始")
          .evaluate((n) => parseFloat(getComputedStyle(n).fontSize)),
      ).toBeGreaterThanOrEqual(16);
    }
    await table
      .getByRole("button", { name: "发布周课表", exact: true })
      .click();
    await page.getByText("周课表已发布，日期例外仍优先。").waitFor();
    await page.screenshot({
      path: `../.qa/mobile-${width}-settings.png`,
      fullPage: true,
    });
    await navigate("大屏控制");
    await page
      .getByRole("heading", { name: "大屏控制", exact: true })
      .waitFor();
    await page
      .getByRole("button", { name: "绑定教室电脑", exact: true })
      .click();
    await noOverflow(page);
    await page.screenshot({
      path: `../.qa/mobile-${width}-pairing.png`,
      fullPage: true,
    });
    expect(errors).toEqual([]);
  });
}
