import { describe, it, expect } from "vitest";
import { shouldBlockNavigation } from "./navigation-guard";

describe("shouldBlockNavigation", () => {
  it("bloquea la navegación solo cuando hay cambios sin guardar y la vista es editable", () => {
    expect(shouldBlockNavigation(true, false)).toBe(true);
    expect(shouldBlockNavigation(true, true)).toBe(false);
    expect(shouldBlockNavigation(false, false)).toBe(false);
    expect(shouldBlockNavigation(false, true)).toBe(false);
  });
});
