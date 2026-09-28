import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listSchools, type SchoolSummary } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { SchoolRegistryPanel } from "../../_components/schools/school-registry-panel";

export const metadata: Metadata = { title: "Escuelas · PlanCope Central" };

export default async function SchoolsPage() {
  const user = await getSessionUser();

  let schools: SchoolSummary[];
  try {
    schools = await listSchools();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Escuelas"
        title="Escuelas"
        description="Administrá el padrón de escuelas de la instalación."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Escuelas" }]}
      />

      <SchoolRegistryPanel initialSchools={schools} user={user!} />
    </>
  );
}
