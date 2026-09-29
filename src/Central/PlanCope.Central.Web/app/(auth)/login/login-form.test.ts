import { createElement } from "react";
import { renderToString } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { replace, refresh } = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn() }));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, refresh })
}));

import { LoginForm } from "./login-form";

describe("LoginForm server render", () => {
  beforeEach(() => {
    replace.mockReset();
    refresh.mockReset();
  });

  it("posts the form and disables submit until hydration", () => {
    const html = renderToString(createElement(LoginForm, { redirectTo: "/dashboard", expired: false }));

    expect(html).toMatch(/<form[^>]*method="post"/);
    expect(html).toMatch(/<button[^>]*type="submit"[^>]*disabled=""/);
  });
});
