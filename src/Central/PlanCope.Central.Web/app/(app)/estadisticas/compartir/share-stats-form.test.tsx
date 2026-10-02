// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ShareStatsForm } from "./share-stats-form";

const { createStatsShare, revokeStatsShare, callCentral } = vi.hoisted(() => ({ createStatsShare: vi.fn(), revokeStatsShare: vi.fn(), callCentral: vi.fn() }));
vi.mock("../../../_lib/shares/client", () => ({ createStatsShare, revokeStatsShare }));
vi.mock("../../../_lib/api/client", () => ({ callCentral }));

afterEach(() => { cleanup(); createStatsShare.mockReset(); revokeStatsShare.mockReset(); callCentral.mockReset(); });

describe("ShareStatsForm", () => {
  it("posts allowlisted grouping and fixed filters, then supports revocation", async () => {
    createStatsShare.mockResolvedValue({ id: "share-id", url: "/estadisticas/compartidas/secret-token", expiresAt: "2026-10-08T10:00:00Z" });
    revokeStatsShare.mockResolvedValue(undefined);
    callCentral.mockImplementation((path: string) => {
      const dimension = new URLSearchParams(path.split("?")[1]).get("dimension");
      const value = dimension === "year" ? "2026" : "4° grado";
      return Promise.resolve({ page: 1, pageSize: 50, totalCount: 1, items: [{ value, label: value }] });
    });
    render(<ShareStatsForm />);

    fireEvent.change(screen.getByLabelText("Agrupar los resultados por"), { target: { value: "subject" } });
    await waitFor(() => expect(callCentral).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText("Seleccionar año lectivo"), { target: { value: "2026" } });
    fireEvent.change(screen.getByLabelText("Seleccionar curso"), { target: { value: "4° grado" } });
    fireEvent.click(screen.getByRole("button", { name: "Crear enlace" }));

    await waitFor(() => expect(createStatsShare).toHaveBeenCalledWith({ groupBy: "subject", filters: { schoolYear: "2026", course: "4° grado" }, metrics: ["attemptCount", "weightedScorePercent"] }));
    expect((await screen.findByLabelText("Enlace público") as HTMLInputElement).value).toContain("secret-token");
    fireEvent.click(screen.getByRole("button", { name: "Revocar" }));
    await waitFor(() => expect(revokeStatsShare).toHaveBeenCalledWith("share-id"));
    expect((await screen.findByRole("status")).textContent).toContain("El enlace fue revocado.");
  });
});
