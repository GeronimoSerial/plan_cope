import { describe, expect, it } from "vitest";
import { resolveStatsCue } from "./HostApp";

describe("resolveStatsCue", () => {
  it("uses the entered CUE only when it is complete and valid", () => {
    expect(resolveStatsCue("12345", ["180055400", "180055401"])).toBe("180055400");
    expect(resolveStatsCue("180055402", ["180055400"])).toBe("180055402");
    expect(resolveStatsCue("", [])).toBe("");
  });
});
