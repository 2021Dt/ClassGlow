/** 图片只按受鉴权的班级接口读取，失败时整次刷新保留旧模型。 */
const cache = new Map<string, Promise<string>>();
export function clearAssetCache() {
  cache.clear();
}
export async function hydrateAssets<T>(
  value: T,
  classId: string,
  token: string,
): Promise<T> {
  async function image(reference: string) {
    const id = reference.slice(6);
    if (!/^[0-9a-f]{64}$/.test(id))
      throw new Error("看板图片引用无效，请重新发布布局。");
    const key = `${token}:${classId}:${id}`;
    if (!cache.has(key)) {
      const pending = (async () => {
        const response = await fetch(
          `/api/classes/${encodeURIComponent(classId)}/assets/${id}`,
          {
            headers: { Authorization: `Bearer ${token}` },
            signal: AbortSignal.timeout(15000),
          },
        );
        if (!response.ok)
          throw new Error("看板图片未能读取，当前内容已保留，请重新刷新。");
        const blob = await response.blob();
        if (
          blob.size > 512 * 1024 ||
          !/^image\/(jpeg|png|webp)$/.test(blob.type)
        )
          throw new Error("看板图片格式或大小无效。");
        const bytes = await blob.arrayBuffer();
        const hash = Array.from(
          new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)),
          (n) => n.toString(16).padStart(2, "0"),
        ).join("");
        if (hash !== id) throw new Error("看板图片校验未通过，请重新刷新。");
        return await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result));
          reader.onerror = () => reject(new Error("图片读取失败。"));
          reader.readAsDataURL(blob);
        });
      })();
      cache.set(key, pending);
      void pending.catch(() => {
        if (cache.get(key) === pending) cache.delete(key);
      });
      while (cache.size > 64) cache.delete(cache.keys().next().value!);
    }
    return await cache.get(key)!;
  }
  async function visit(item: unknown): Promise<unknown> {
    if (Array.isArray(item)) return await Promise.all(item.map(visit));
    if (item && typeof item === "object") {
      return Object.fromEntries(
        await Promise.all(
          Object.entries(item).map(async ([key, child]) => [
            key,
            ["image", "backgroundImage", "panelImage"].includes(key) &&
            typeof child === "string" &&
            child.startsWith("asset:")
              ? await image(child)
              : await visit(child),
          ]),
        ),
      );
    }
    return item;
  }
  return (await visit(value)) as T;
}
