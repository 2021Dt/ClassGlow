import { describe, expect, it } from "vitest";
import {
  defaultNoticeStyle,
  readNoticeStyles,
  supportsNoticeDesign,
} from "./screenNotice";

describe("saved notice designs", () => {
  it("rejects damaged styles and limits local storage without restoring unrelated data", () => {
    const valid = {
      id: "test",
      name: "课堂",
      mode: "popup",
      style: { ...defaultNoticeStyle(), unknown: "discard" },
      message: "private",
      overFullscreen: true,
    };
    const rows = readNoticeStyles(
      JSON.stringify([
        { ...valid, style: { ...valid.style, fontSize: 10000 } },
        valid,
      ]),
    );
    expect(rows).toHaveLength(1);
    expect(rows[0]).not.toHaveProperty("message");
    expect(rows[0]).not.toHaveProperty("overFullscreen");
    expect(rows[0]?.style).not.toHaveProperty("unknown");
    expect(readNoticeStyles("broken")).toEqual([]);
    expect(
      readNoticeStyles(
        JSON.stringify(
          Array.from({ length: 15 }, (_, i) => ({
            ...valid,
            id: String(i),
          })),
        ),
      ),
    ).toHaveLength(12);
  });
  it("only enables design delivery for clients that can render it", () => {
    for (const v of ["0.4.12", "0.3.99", "", "unknown"])
      expect(supportsNoticeDesign(v)).toBe(false);
    for (const v of ["0.4.13", "0.4.100", "0.5.0", "1.0.0"])
      expect(supportsNoticeDesign(v)).toBe(true);
  });
});
