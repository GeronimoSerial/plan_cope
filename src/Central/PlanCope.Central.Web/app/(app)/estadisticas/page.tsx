import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isScopeDenied, isSessionExpired, listSchoolStats, listSchoolYears, type SchoolStatsRow, type SchoolYearOption } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { StatsPanel } from "../../_components/stats/stats-panel";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";

export const metadata: Metadata = { title: "Estadísticas · PlanCope Central" };

export default async function StatsPage() {
  let schools: SchoolStatsRow[];
  let schoolYears: SchoolYearOption[] = [];
  let scopeDenied = false;
  try {
    [schools, schoolYears] = await Promise.all([listSchoolStats(), listSchoolYears()]);
  } catch (error) {
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired("/estadisticas");
    }
    if (isScopeDenied(error)) {
      schools = [];
      scopeDenied = true;
    } else {
      throw error;
    }
  }

  return (
    <>
      <PageHeader
        title="Estadísticas"
        description="Los datos llegan al sincronizar los equipos."
      />
      <StatsPanel initialSchools={schools} schoolYears={schoolYears} scopeDenied={scopeDenied} />
    </>
  );
}
