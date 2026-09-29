import { describe, expect, it } from "vitest";
import { safeInternalPath } from "./safe-redirect";

describe("safeInternalPath", () => {
  it.each(["/\\evil.com", "/\t/evil.com", "//evil.com", "https://evil.com", "/%5Cevil.com", "/%09/evil.com", "/\\\\evil.com", "javascript:alert(1)", "", null])("rejects %s", value => {
    expect(safeInternalPath(value, "https://central.example")).toBe("/dashboard");
  });
  it.each(["/exams", "/exams/ex_1/versions/v_1/builder?tab=x"])("accepts %s", value => {
    expect(safeInternalPath(value, "https://central.example")).toBe(value);
  });
});
