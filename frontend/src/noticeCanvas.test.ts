import { describe, expect, it } from "vitest";
import {
  newNoticeElement,
  defaultNoticeCanvas,
  clampNoticeElement,
  resizeNoticeElement,
  canvasProblem,
  readNoticeCanvases,
  supportsNoticeCanvas,
} from "./noticeCanvas";

describe("reminder canvas boundaries", () => {
  it("keeps move and resize within the canvas while fixing the opposite edge", () => {
    const item = newNoticeElement("card", { x: 300, y: 250, w: 800, h: 400 });
    expect(clampNoticeElement({ ...item, x: 2000, y: -20 })).toMatchObject({
      x: 800,
      y: 0,
      w: 800,
    });
    const resized = resizeNoticeElement(item, "nw", 2000, -2000);
    expect(resized).toMatchObject({ x: 1052, y: 0, w: 48, h: 650 });
    expect(resized.x + resized.w).toBe(item.x + item.w);
    expect(resizeNoticeElement(item, "se", 2000, 2000)).toMatchObject({
      x: 300,
      y: 250,
      w: 1300,
      h: 650,
    });
  });
  it("rejects invisible, unsafe and oversize designs before save or send", () => {
    const canvas = defaultNoticeCanvas();
    expect(canvasProblem(canvas)).toBe("");
    expect(
      canvasProblem({
        ...canvas,
        elements: canvas.elements.map((e) => ({ ...e, hidden: true })),
      }),
    ).not.toBe("");
    expect(
      canvasProblem({
        ...canvas,
        elements: [
          newNoticeElement("image", { image: "https://example.com/x.jpg" }),
        ],
      }),
    ).not.toBe("");
    const large = "data:image/jpeg;base64," + "A".repeat(200000);
    expect(
      canvasProblem({
        ...canvas,
        elements: Array.from({ length: 3 }, () =>
          newNoticeElement("image", { image: large }),
        ),
      }),
    ).toContain("512 KB");
    expect(readNoticeCanvases("[null,{},false]")).toEqual([]);
    expect(
      readNoticeCanvases(JSON.stringify([{ id: "one", name: "课前", canvas }])),
    ).toHaveLength(1);
    expect(supportsNoticeCanvas("0.4.13")).toBe(false);
    expect(supportsNoticeCanvas("0.4.14")).toBe(true);
  });
});
