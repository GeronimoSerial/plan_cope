import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isScopeDenied, listLiveSessions, type LiveSessionSummary } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { LiveSessionsPanel } from "../../_components/sessions/live-sessions-panel";

export const metadata: Metadata = { title: "Sesiones y señales · PlanCope Central" };

export default async function LiveSessionsPage() {
  const user = await getSessionUser();
  if (!user || user.role !== "Admin") redirect("/dashboard");
  let sessions: LiveSessionSummary[] = [];
  let readStatus: "denied" | "error" | null = null;
  try { sessions = await listLiveSessions(); } catch (error) { readStatus = isScopeDenied(error) ? "denied" : "error"; }

  return (
    <>
      <PageHeader title="Sesiones y señales" description="Estado informado por los nodos Local y frescura del último heartbeat." />
      <LiveSessionsPanel initialSessions={sessions} initialReadStatus={readStatus} />
    </>
  );
}
