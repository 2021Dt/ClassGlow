import { afterEach, expect, it, vi } from "vitest";
import { clearAssetCache, hydrateAssets } from "./assetCache";

afterEach(() => {
  clearAssetCache();
  vi.unstubAllGlobals();
});

it("图片按账号和班级缓存并校验内容摘要，正文中的引用文本保持原样", async () => {
  const bytes = new TextEncoder().encode("test-image");
  const id = Array.from(
    new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)),
    (n) => n.toString(16).padStart(2, "0"),
  ).join("");
  const fetch = vi.fn(
    async (_url: string, _options?: RequestInit) =>
      new Response(bytes, { headers: { "Content-Type": "image/jpeg" } }),
  );
  vi.stubGlobal("fetch", fetch);
  vi.stubGlobal(
    "FileReader",
    class {
      result = "";
      onload?: () => void;
      async readAsDataURL(blob: Blob) {
        this.result =
          `data:${blob.type};base64,` +
          btoa(
            String.fromCharCode(...new Uint8Array(await blob.arrayBuffer())),
          );
        this.onload?.();
      }
    },
  );
  const layout = {
    backgroundImage: "asset:" + id,
    canvas: [{ image: "asset:" + id }],
    text: "asset:keep-as-text",
  };
  const resolved = await hydrateAssets(layout, "class-a", "token-a");
  expect(resolved.backgroundImage).toMatch(/^data:image\/jpeg;base64,/);
  expect(resolved.canvas[0]!.image).toBe(resolved.backgroundImage);
  expect(resolved.text).toBe("asset:keep-as-text");
  await hydrateAssets(layout, "class-a", "token-a");
  expect(fetch).toHaveBeenCalledTimes(1);
  await hydrateAssets(layout, "class-b", "token-a");
  expect(fetch).toHaveBeenCalledTimes(2);
  await hydrateAssets(layout, "class-a", "token-b");
  expect(fetch).toHaveBeenCalledTimes(3);
  expect(fetch.mock.calls[0]![0]).not.toContain("token-a");
});

it("损坏图片不进入缓存，失败后可以重新获取", async () => {
  const fetch = vi.fn(
    async () =>
      new Response("wrong-bytes", {
        headers: { "Content-Type": "image/jpeg" },
      }),
  );
  vi.stubGlobal("fetch", fetch);
  const layout = { image: "asset:" + "a".repeat(64) };
  await expect(hydrateAssets(layout, "class-a", "token")).rejects.toThrow(
    "校验",
  );
  await expect(hydrateAssets(layout, "class-a", "token")).rejects.toThrow(
    "校验",
  );
  expect(fetch).toHaveBeenCalledTimes(2);
});
