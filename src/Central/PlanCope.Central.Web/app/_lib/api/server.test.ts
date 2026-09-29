import { beforeEach, describe, expect, it, vi } from "vitest";

const { centralFetch, getAccessToken } = vi.hoisted(() => ({
  centralFetch: vi.fn(),
  getAccessToken: vi.fn()
}));

vi.mock("server-only", () => ({}));
vi.mock("../server/central-server", () => ({ centralFetch }));
vi.mock("../server/session", () => ({ getAccessToken }));

import { isNotFound, listVersions } from "./server";

describe("server API not found handling", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getAccessToken.mockResolvedValue("test-token");
  });

  it("identifies a 404 response as not found", async () => {
    centralFetch.mockResolvedValue(new Response("{}", { status: 404 }));

    let caught: unknown;
    try {
      await listVersions("missing-exam");
    } catch (error) {
      caught = error;
    }

    expect(isNotFound(caught)).toBe(true);
  });

  it("does not classify other API errors as not found", async () => {
    centralFetch.mockResolvedValue(new Response("{}", { status: 500 }));

    let caught: unknown;
    try {
      await listVersions("exam-id");
    } catch (error) {
      caught = error;
    }

    expect(isNotFound(caught)).toBe(false);
  });
});
