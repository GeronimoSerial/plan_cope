"use client";

import type { StatsShareCreateRequest, StatsShareCreated } from "./contracts";

export async function createStatsShare(request: StatsShareCreateRequest): Promise<StatsShareCreated> {
  const response = await fetch("/api/central/admin/stats/shares", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: unknown } | null;
    if (response.status === 403) throw new Error("Tu cuenta no tiene alcance para crear este enlace.");
    if (typeof body?.error === "string") throw new Error(body.error);
    throw new Error("No se pudo crear el enlace.");
  }
  return response.json() as Promise<StatsShareCreated>;
}

export async function revokeStatsShare(id: string): Promise<void> {
  const response = await fetch(`/api/central/admin/stats/shares/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!response.ok && response.status !== 404) throw new Error("No se pudo revocar el enlace.");
}
