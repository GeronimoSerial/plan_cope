import type { Metadata } from "next";
import { PageHeader } from "../../_components/layout/page-header";
import { StatsPanel } from "../../_components/stats/stats-panel";

export const metadata: Metadata = { title: "Estadísticas · PlanCope Central" };

export default async function StatsPage() {
  return (
    <>
      <PageHeader
        title="Estadísticas"
        description="Explorá resultados agregados por territorio, curso, materia, año y establecimiento."
      />
      <StatsPanel />
    </>
  );
}
