import { describe, expect, it } from "vitest";
import { restoreLocalDraft, type Draft } from "./domain";

const draft: Draft = {
  id: "local-draft",
  kind: "homework",
  classIds: ["class-1"],
  subject: "语文",
  body: "",
  from: "2026-11-02T08:00:00+08:00",
  until: "2026-11-03T08:00:00+08:00",
  due: "",
  revision: 0,
  updated: "2026-11-02T00:00:00Z",
  reason: "",
  priority: false,
};

describe("本机草稿恢复", () => {
  it("拒绝损坏结构，避免恢复时崩溃或把损坏字段发往服务端", () => {
    for (const value of [
      null,
      [],
      { body: "仅有正文" },
      { ...draft, classIds: "class-1" },
      { ...draft, from: "损坏时间" },
      { ...draft, due: "损坏时间" },
      { ...draft, revision: -1 },
      { ...draft, priority: "false" },
      { ...draft, kind: "unsupported" },
    ])
      expect(restoreLocalDraft(value)).toBeNull();
  });
  it("保留尚未填写和超长正文，兼容旧字段并只恢复已知字段", () => {
    expect(restoreLocalDraft(draft)?.body).toBe("");
    const body = "未完成的正文".repeat(200);
    const recovered = restoreLocalDraft({
      ...draft,
      body,
      priority: undefined,
      reason: undefined,
      unexpectedField: "不会带入发布请求",
    });
    expect(recovered?.body).toBe(body);
    expect(recovered?.priority).toBe(false);
    expect(recovered?.reason).toBe("");
    expect(recovered).not.toHaveProperty("unexpectedField");
  });
});
