import { afterEach, expect, it, vi } from "vitest";

afterEach(() => {
  vi.unstubAllGlobals();
});
it("切换账号后迟到的刷新不能覆盖新账号资料", async () => {
  const storage = {
    getItem: () => null,
    setItem: vi.fn(),
    removeItem: vi.fn(),
  };
  vi.stubGlobal("localStorage", storage);
  vi.stubGlobal("sessionStorage", storage);
  vi.stubGlobal("window", { addEventListener: vi.fn() });
  let release!: (value: Response) => void;
  vi.stubGlobal(
    "fetch",
    () =>
      new Promise<Response>((resolve) => {
        release = resolve;
      }),
  );
  const { refreshModel, session, setSession } = await import("./api");
  setSession("old-token");
  session.userId = "old-user";
  const pending = refreshModel();
  setSession("new-token");
  expect(session.userId).toBe("");
  expect(session.name).toBe("");
  session.userId = "new-user";
  release(
    new Response(
      JSON.stringify({
        schema: 1,
        user: { id: "old-user", name: "旧账号" },
        classes: [],
        contents: [],
        drafts: [],
        devices: [],
        commands: [],
      }),
    ),
  );
  await expect(pending).rejects.toMatchObject({ code: "session_changed" });
  expect(session.userId).toBe("new-user");
  expect(session.token).toBe("new-token");
});
