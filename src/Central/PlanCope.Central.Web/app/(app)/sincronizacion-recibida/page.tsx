import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { listReceivedSyncAttempts } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { ReceivedSyncPanel } from "../../_components/sync-admin/received-sync-panel";

export const metadata: Metadata = { title: "Sincronización recibida · PlanCope Central" };

export default async function ReceivedSyncPage() {
  const user = await getSessionUser();
  if (!user || user.role !== "Admin") redirect("/dashboard");
  const data = await listReceivedSyncAttempts();

  return (
    <>
      <PageHeader title="Sincronización recibida" description="Intentos que llegaron a Central y estado de su calificación y atribución." />
      <ReceivedSyncPanel initialPage={data} />
    </>
  );
}
