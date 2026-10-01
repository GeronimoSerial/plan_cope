import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { listLiveSessions, type LiveSessionSummary } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";

export const metadata: Metadata = { title: "Sesiones en curso · PlanCope Central" };

function formatDate(value: string | null) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("es-AR", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
}

export default async function LiveSessionsPage() {
  const user = await getSessionUser();
  if (!user || user.role !== "Admin") redirect("/dashboard");
  let sessions: LiveSessionSummary[] = [];
  try { sessions = await listLiveSessions(); } catch { sessions = []; }

  return (
    <>
      <PageHeader title="Sesiones en curso" description="Actividad informada por los nodos Local. Los resultados finales se muestran en Estadísticas cuando llegan los intentos." />
      {sessions.length === 0 ? (
        <p className="rounded-lg border p-4 text-sm text-muted-foreground">No hay sesiones abiertas informadas por los nodos.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead><tr className="border-b text-left"><th className="p-3">CUE</th><th className="p-3">Estado</th><th className="p-3">Alumnos</th><th className="p-3">En evaluación</th><th className="p-3">Entregados</th><th className="p-3">Cerrados/forzados</th><th className="p-3">Última señal</th></tr></thead>
            <tbody>{sessions.map(session => <tr key={`${session.cue}-${session.sessionId}`} className="border-b last:border-0">
              <td className="p-3 font-mono">{session.cue}</td>
              <td className="p-3"><span className={session.signalStatus === "En curso" ? "text-emerald-700 dark:text-emerald-300" : "text-amber-700 dark:text-amber-300"}>{session.signalStatus}</span></td>
              <td className="p-3">{session.joinedCount}</td><td className="p-3">{session.inProgressCount}</td><td className="p-3">{session.submittedCount}</td><td className="p-3">{session.closedOrForcedCount}</td><td className="p-3">{formatDate(session.lastHeartbeatAt)}</td>
            </tr>)}</tbody>
          </table>
        </div>
      )}
    </>
  );
}
