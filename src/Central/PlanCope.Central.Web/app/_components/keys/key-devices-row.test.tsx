// @vitest-environment jsdom

import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ActivationKeysTable } from "./activation-keys-table";

const mocks = vi.hoisted(() => ({
  callCentral: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn()
}));

vi.mock("../../_lib/api/client", () => ({ callCentral: mocks.callCentral }));
vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: mocks.toastSuccess }
}));
vi.mock("@/components/ui/button", () => ({
  Button: ({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: string; size?: string }) => (
    <button {...props}>{children}</button>
  )
}));
vi.mock("@/components/ui/table", () => {
  const Cell = ({ children, colSpan, ...props }: HTMLAttributes<HTMLDivElement> & { colSpan?: number }) => (
    <div {...props} data-col-span={colSpan}>{children}</div>
  );
  const Row = ({ children, ...props }: HTMLAttributes<HTMLDivElement>) => <div {...props}>{children}</div>;
  return {
    Table: ({ children }: { children: ReactNode }) => <div>{children}</div>,
    TableBody: Row,
    TableCell: Cell,
    TableHead: Cell,
    TableHeader: Row,
    TableRow: Row
  };
});
vi.mock("@/components/ui/badge", () => ({
  Badge: ({ children }: { children: ReactNode }) => <span>{children}</span>
}));
vi.mock("@/components/ui/dropdown-menu", () => ({
  DropdownMenu: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuItem: ({ children, onClick }: { children: ReactNode; onClick?: () => void }) => (
    <button onClick={onClick}>{children}</button>
  ),
  DropdownMenuTrigger: ({ children }: { children: ReactNode }) => <button aria-label="Acciones">{children}</button>
}));
vi.mock("@/components/ui/alert-dialog", () => ({
  AlertDialog: ({ open, children }: { open: boolean; children: ReactNode }) =>
    open ? <div role="dialog">{children}</div> : null,
  AlertDialogAction: ({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) => (
    <button {...props}>{children}</button>
  ),
  AlertDialogCancel: ({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) => (
    <button {...props}>{children}</button>
  ),
  AlertDialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AlertDialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  AlertDialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AlertDialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AlertDialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>
}));
vi.mock("../help/term-hint", () => ({
  TermLabel: ({ children }: { children: ReactNode }) => <span>{children}</span>
}));

const keyRecord = {
  id: "key-1",
  keyPrefix: "PCOPE-01",
  issuedAt: "2026-09-30T12:00:00.000Z",
  expiresAt: null,
  maxActivations: 3,
  activationCount: 1,
  revokedAt: null,
  revokedReason: null,
  note: null,
  holderName: "Ana Pérez"
};

const device = {
  id: "node-1",
  nodeCode: "NODE-123",
  deviceName: "Equipo de aula",
  enrolledAt: "2026-09-29T12:00:00.000Z",
  lastSeenAt: "2026-09-30T12:00:00.000Z",
  appVersion: "1.2.3",
  status: "Active",
  revokedAt: null
};

function renderTable() {
  return render(
    <ActivationKeysTable
      keys={[keyRecord]}
      onRevoke={vi.fn()}
      onReissue={vi.fn()}
      onCreate={vi.fn()}
    />
  );
}

async function expandAndLoadDevice() {
  expect(mocks.callCentral).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Mostrar equipos activados" }));
  await screen.findByText("Equipo de aula");
  expect(mocks.callCentral).toHaveBeenCalledWith("admin/activation/keys/key-1/nodes");
}

afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

describe("ActivationKeysTable device details", () => {
  it("expands the key and loads its activated devices", async () => {
    mocks.callCentral.mockResolvedValueOnce([device]);
    renderTable();

    expect(screen.getByText("Equipos (usados/máximo)")).toBeTruthy();
    await expandAndLoadDevice();
    expect(screen.getByText(/NODE-123/)).toBeTruthy();
    expect(screen.getByText(/Versión 1.2.3/)).toBeTruthy();
  });

  it("asks for confirmation before revoking a device", async () => {
    mocks.callCentral.mockResolvedValueOnce([device]).mockResolvedValueOnce(undefined);
    renderTable();
    await expandAndLoadDevice();

    fireEvent.click(screen.getByRole("button", { name: "Revocar equipo" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(/dejará de sincronizarse/)).toBeTruthy();
    expect(mocks.callCentral).toHaveBeenCalledTimes(1);

    fireEvent.click(within(dialog).getByRole("button", { name: "Revocar equipo" }));
    await waitFor(() => expect(mocks.callCentral).toHaveBeenCalledWith(
      "admin/activation/nodes/node-1/revoke",
      expect.objectContaining({ method: "POST" })
    ));
    await screen.findByText("Equipo revocado");
    expect(mocks.toastSuccess).toHaveBeenCalledWith("Equipo revocado.");
  });

  it("shows the API error when device revocation fails", async () => {
    mocks.callCentral
      .mockResolvedValueOnce([device])
      .mockRejectedValueOnce(new Error("No se pudo conectar"));
    renderTable();
    await expandAndLoadDevice();

    fireEvent.click(screen.getByRole("button", { name: "Revocar equipo" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Revocar equipo" }));

    await waitFor(() => expect(mocks.toastError).toHaveBeenCalledWith("No se pudo conectar"));
    const dialog = screen.getByRole("dialog");
    expect(dialog).toBeTruthy();
    expect(within(dialog).getByRole("button", { name: "Revocar equipo" })).toBeTruthy();
  });

  it("reports a failure to load the device list", async () => {
    mocks.callCentral.mockRejectedValueOnce(new Error("Error de lectura"));
    renderTable();
    fireEvent.click(screen.getByRole("button", { name: "Mostrar equipos activados" }));

    await waitFor(() => expect(mocks.toastError).toHaveBeenCalledWith("Error de lectura"));
  });
});
