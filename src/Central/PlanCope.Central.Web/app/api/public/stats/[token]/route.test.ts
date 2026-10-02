import { afterEach, describe, expect, it, vi } from "vitest";
import { GET } from "./route";

const { centralFetch } = vi.hoisted(() => ({ centralFetch: vi.fn() }));
vi.mock("../../../../_lib/server/central-server", () => ({ centralFetch }));

afterEach(() => centralFetch.mockReset());

describe("public statistics BFF", () => {
  it("forwards only the anonymous share token and never attaches credentials", async () => {
    centralFetch.mockResolvedValue(new Response(JSON.stringify({ rows: [] }), { status: 200, headers: { "Content-Type": "application/json" } }));
    const token = "A".repeat(43);
    const response = await GET(new Request("https://central.example/api/public"), { params: Promise.resolve({ token }) });
    expect(response.status).toBe(200);
    expect(centralFetch).toHaveBeenCalledWith(`/api/public/stats/${token}`);
    expect(response.headers.get("cache-control")).toContain("no-store");
    expect(response.headers.get("referrer-policy")).toBe("no-referrer");
    expect(response.headers.get("x-robots-tag")).toContain("noindex");
  });

  it("normalizes malformed tokens to the same public 404", async () => {
    const response = await GET(new Request("https://central.example/api/public"), { params: Promise.resolve({ token: "bad" }) });
    expect(response.status).toBe(404);
    expect(centralFetch).not.toHaveBeenCalled();
  });
});
