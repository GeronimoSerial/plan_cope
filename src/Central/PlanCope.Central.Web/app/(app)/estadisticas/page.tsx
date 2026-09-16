import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listSchoolStats, type SchoolStatsRow } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { StatsPanel } from "../../_components/stats/stats-panel";

export const metadata: Metadata = { title: "Estadísticas · PlanCope Central" };

export default async function StatsPage() {
  let schools: SchoolStatsRow[];
  try {
    schools = await listSchoolStats();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Estadísticas"
        title="Estadísticas"
        description="Estadísticas por establecimiento, curso y examen."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Estadísticas" }]}
      />

      <StatsPanel initialSchools={schools} />
    </>
  );
}
