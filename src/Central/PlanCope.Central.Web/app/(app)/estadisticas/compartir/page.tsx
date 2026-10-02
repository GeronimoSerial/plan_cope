import { redirect } from "next/navigation";
import { PageHeader } from "../../../_components/layout/page-header";
import { getSessionUser } from "../../../_lib/server/session";
import { ShareStatsForm } from "./share-stats-form";

export const metadata = { title: "Compartir estadísticas · PlanCope Central" };

export default async function ShareStatsPage() {
  const user = await getSessionUser();
  if (user?.role !== "Admin") redirect("/estadisticas");
  return <>
    <PageHeader title="Compartir estadísticas" description="Creá un enlace de solo lectura con una copia fija de resultados agregados." />
    <ShareStatsForm />
  </>;
}
