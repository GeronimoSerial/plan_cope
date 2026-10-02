import type { Metadata } from "next";
import { PageHeader } from "../../_components/layout/page-header";
import { StatsPanel } from "../../_components/stats/stats-panel";
import Link from "next/link";
import { getSessionUser } from "../../_lib/server/session";

export const metadata: Metadata = { title: "Estadísticas · PlanCope Central" };

export default async function StatsPage() {
  const user = await getSessionUser();
  return (
    <>
      <PageHeader
        title="Estadísticas"
        description="Explorá resultados agregados por territorio, curso, materia, año y establecimiento."
        actions={user?.role === "Admin" ? <Link className="rounded-md border px-3 py-2 text-sm font-medium hover:bg-muted" href="/estadisticas/compartir">Compartir estadísticas</Link> : undefined}
      />
      <StatsPanel />
    </>
  );
}
