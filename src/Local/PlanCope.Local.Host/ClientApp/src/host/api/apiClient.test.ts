import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient } from "./apiClient";

describe("ApiClient AbortSignal handling", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("does not pass a click event as the fetch signal", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ json: async () => ({ status: "up_to_date" }) });
    vi.stubGlobal("fetch", fetchMock);

    await new ApiClient("http://localhost").pullExams({ type: "click" } as unknown as AbortSignal);

    expect(fetchMock.mock.calls[0]?.[1]).not.toHaveProperty("signal");
  });

  it("passes through a real AbortSignal", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ json: async () => ({ status: "up_to_date" }) });
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();

    await new ApiClient("http://localhost").pullExams(controller.signal);

    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ signal: controller.signal });
  });
});
