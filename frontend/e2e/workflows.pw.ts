import {
  test,
  expect,
  type APIRequestContext,
  type Page,
} from "@playwright/test";

const password = "E2e-Temporary-Only!2026";
async function account(request: APIRequestContext, prefix: string) {
  const login = `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const admin = await request.post("/api/auth/login", {
    data: { login: "e2e_admin", password, clientId: "e2e-admin-fixture" },
  });
  expect(admin.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await admin.json()).token}` };
  const created = await request.post("/api/system/accounts", {
    headers,
    data: { login, password, name: "回归老师", identity: "headteacher" },
  });
  expect(created.status()).toBe(201);
  return login;
}
async function login(page: Page, name: string) {
  await page.goto("/");
  await page.getByLabel("账号", { exact: true }).fill(name);
  await page.getByLabel("密码", { exact: true }).fill(password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.locator(".app-shell")).toBeVisible();
}
async function noOverflow(page: Page) {
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
  expect(
    await page
      .locator("dialog[open]")
      .evaluateAll((nodes) =>
        nodes.every((n) => n.scrollWidth <= n.clientWidth + 1),
      ),
  ).toBeTruthy();
}
async function navigate(page: Page, name: string) {
  if (name === "发布记录" && page.viewportSize()!.width <= 650) {
    await page
      .getByRole("navigation", { name: "手机底部导航" })
      .getByRole("button", { name: "记录", exact: true })
      .click();
    return;
  }
  if (page.viewportSize()!.width > 650)
    await page
      .getByRole("navigation", { name: "主要导航" })
      .getByRole("button", { name, exact: true })
      .click();
  else {
    await page.getByRole("button", { name: "我的", exact: true }).click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name, exact: true })
      .click();
  }
}

for (const width of [320, 390, 768]) {
  test(`${width}px 管理员开户后的登录、发布、离线草稿、返回与持久登录`, async ({
    page,
    context,
    request,
  }) => {
    await page.setViewportSize({ width, height: 844 });
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    const name = await account(request, `mobile${width}`);
    await login(page, name);
    await page.getByRole("button", { name: "创建第一个班级" }).click();
    await page.getByLabel("班级名称").fill("端到端回归班（非真实数据）");
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "创建班级", exact: true })
      .click();
    await expect(
      page.getByRole("button", { name: "发作业", exact: true }),
    ).toBeVisible();
    await page.getByRole("button", { name: "暂时跳过", exact: true }).click();
    await noOverflow(page);
    const token = await page.evaluate(() =>
      localStorage.getItem("kanban-session"),
    );
    expect(token).toBeTruthy();
    const headers = { Authorization: `Bearer ${token}` };
    await page.getByRole("button", { name: "发作业", exact: true }).click();
    await page
      .getByLabel("作业内容", { exact: false })
      .fill("端到端发布：最后输入的完整正文。");
    await noOverflow(page);
    await page
      .getByRole("button", { name: "发布到 1 个班", exact: true })
      .click();
    await expect(page.locator("dialog.editor-dialog")).toHaveCount(0);
    const state = await (
      await request.get("/api/bootstrap", { headers })
    ).json();
    expect(state.contents).toHaveLength(1);
    expect(state.contents[0].body).toBe("端到端发布：最后输入的完整正文。");
    await page.getByRole("button", { name: "发作业", exact: true }).click();
    await context.setOffline(true);
    await page
      .getByLabel("作业内容", { exact: false })
      .fill("离线输入，联网后仅保存草稿。");
    await expect
      .poll(() =>
        page.evaluate(() =>
          Object.keys(localStorage).some((k) => k.startsWith("kanban-input-")),
        ),
      )
      .toBeTruthy();
    await context.setOffline(false);
    await page
      .getByRole("button", { name: "保存草稿并关闭", exact: true })
      .first()
      .click();
    await expect(page.locator("dialog.editor-dialog")).toHaveCount(0);
    const restored = await (
      await request.get("/api/bootstrap", { headers })
    ).json();
    expect(restored.contents).toHaveLength(1);
    expect(restored.drafts).toHaveLength(1);
    await navigate(page, "发布记录");
    await page.getByLabel("搜索正式内容").fill("完整");
    await expect(page).toHaveURL(/records_search=/);
    await page.reload();
    await expect(page.getByLabel("搜索正式内容")).toHaveValue("完整");
    await expect(
      page.locator(".record-body").filter({ hasText: "最后输入的完整正文" }),
    ).toBeVisible();
    await navigate(page, "教室电脑");
    await expect(page).toHaveURL(/page=devices/);
    await page.reload();
    await expect(page).toHaveURL(/page=devices/);
    await expect(
      page.getByRole("heading", { name: "尚未绑定教室电脑" }),
    ).toBeVisible();
    await noOverflow(page);
    await page.goBack();
    await expect(page).toHaveURL(/page=contents/);
    await page.goto("/?page=overview");
    await expect(
      page.getByRole("button", { name: "发作业", exact: true }),
    ).toBeVisible();
    await expect(page.locator(".teacher-guide-entry")).toHaveCSS(
      "display",
      "flex",
    );
    await page.screenshot({
      path: `../.qa/mobile-${width}-current.png`,
      fullPage: true,
    });
    expect(errors).toEqual([]);
  });
}

test("手机和电脑同时登录、记录筛选刷新、单独撤销设备", async ({
  page,
  browser,
  request,
}) => {
  const name = await account(request, "sessions");
  await page.setViewportSize({ width: 1024, height: 800 });
  await login(page, name);
  const second = await browser.newContext({
    viewport: { width: 390, height: 844 },
    isMobile: true,
    hasTouch: true,
    userAgent:
      "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile Safari/537.36",
  });
  try {
    const phone = await second.newPage();
    await login(phone, name);
    await page.reload();
    await expect(page.locator(".app-shell")).toBeVisible();
    await page.goto("/?page=license");
    await expect(page.getByRole("heading", { name: "登录设备" })).toBeVisible();
    await expect(page.getByText("手机浏览器", { exact: false })).toBeVisible();
    await page.getByRole("button", { name: "退出其他设备" }).click();
    await expect(
      page.locator(".record-row").filter({ hasText: "当前设备" }),
    ).toHaveCount(1);
    await phone.reload();
    await expect(
      phone.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
  } finally {
    await second.close();
  }
});

test("管理员通过网页创建班主任账号，老师拿到账密直接登录", async ({
  page,
  browser,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page, "e2e_admin");
  await page.getByRole("button", { name: "添加老师", exact: true }).click();
  const dialog = page.getByRole("dialog");
  const name = `ui_teacher_${Date.now()}`;
  await dialog.getByLabel("老师姓名").fill("网页开户回归");
  await dialog.getByLabel("登录账号").fill(name);
  await dialog.getByLabel("初始密码").fill(password);
  await dialog.getByRole("combobox", { name: "账号身份" }).click();
  await page.getByRole("option", { name: /班主任/ }).click();
  await dialog.getByRole("button", { name: "创建账号", exact: true }).click();
  await expect(dialog.getByText(name, { exact: true })).toBeVisible();
  const teacher = await browser.newContext();
  try {
    const teacherPage = await teacher.newPage();
    await login(teacherPage, name);
  } finally {
    await teacher.close();
  }
});
