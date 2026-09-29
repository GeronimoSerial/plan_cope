import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listSchoolStats, type SchoolStatsRow } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { StatsPanel } from "../../_components/stats/stats-panel";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";

export const metadata: Metadata = { title: "Estadísticas · PlanCope Central" };

export default async function StatsPage() {
  let schools: SchoolStatsRow[];
  try {
    schools = await listSchoolStats();
  } catch (error) {
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired("/estadisticas");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        title="Estadísticas"
        description="Resultados de exámenes por escuela, curso y examen. Los datos los envían los nodos."
      />
      <StatsPanel initialSchools={schools} />
    </>
  );
}
