"use client";

import { useEffect, useState } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { callCentral, CentralApiError } from "../../_lib/api/client";
import type { LiveSessionSummary } from "../../_lib/api/server";

const POLL_INTERVAL_MS = 30_000;
const dateFormatter = new Intl.DateTimeFormat("es-AR", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "America/Argentina/Buenos_Aires"
});

function formatHeartbeatAge(value: string | null, now: number | null): string {
  if (!value) return "Sin dato";
  if (now === null) return "";
  const seconds = Math.max(0, Math.floor((now - new Date(value).getTime()) / 1000));
  if (seconds < 60) return `hace ${seconds} s`;
  if (seconds < 3600) return `hace ${Math.floor(seconds / 60)} min`;
  const hours = Math.floor(seconds / 3600);
  if (hours < 24) return `hace ${hours} h`;
  return `hace ${Math.floor(hours / 24)} d`;
}

function heartbeatLabel(session: LiveSessionSummary, now: number | null): string {
  if (!session.lastHeartbeatAt || session.signalStatus === "missing") return "Sin señal";
  if (session.signalStatus === "stale") return "Desactualizada";
  if (session.signalStatus !== "fresh") return "Sin señal";

  const staleAfterSeconds = session.heartbeatStaleAfterSeconds;
  if (typeof staleAfterSeconds !== "number" || !Number.isFinite(staleAfterSeconds) || staleAfterSeconds < 0) {
    return "Desactualizada";
  }
  const heartbeatAt = new Date(session.lastHeartbeatAt).getTime();
  if (!Number.isFinite(heartbeatAt)) return "Sin señal";
  if (now === null) return "Fresca";

  const ageSeconds = (now - heartbeatAt) / 1000;
  if (ageSeconds > staleAfterSeconds) return "Desactualizada";
  return "Fresca";
}

function lifecycleLabel(status: string): string {
  if (status === "active") return "Activa";
  if (status === "paused") return "Pausada";
  if (status === "closed") return "Cerrada";
  return status;
}

export function LiveSessionsPanel({ initialSessions, initialReadStatus }: {
  initialSessions: LiveSessionSummary[];
  initialReadStatus: "denied" | "error" | null;
}) {
  const [sessions, setSessions] = useState(initialSessions);
  const [readStatus, setReadStatus] = useState<"denied" | "error" | null>(initialReadStatus);
  const [now, setNow] = useState<number | null>(null);

  useEffect(() => {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let controller: AbortController | undefined;

    const poll = async () => {
      controller = new AbortController();
      try {
        const updated = await callCentral<LiveSessionSummary[]>("admin/live-sessions", { signal: controller.signal });
        if (!stopped) {
          setSessions(updated);
          setReadStatus(null);
          setNow(Date.now());
        }
      } catch (cause) {
        if (!stopped && !controller.signal.aborted) {
          setReadStatus(cause instanceof CentralApiError && cause.status === 403 ? "denied" : "error");
        }
      } finally {
        if (!stopped) timer = setTimeout(() => void poll(), POLL_INTERVAL_MS);
      }
    };

    timer = setTimeout(() => void poll(), POLL_INTERVAL_MS);
    const ageTimer = setInterval(() => setNow(Date.now()), 15_000);
    return () => {
      stopped = true;
      if (timer) clearTimeout(timer);
      clearInterval(ageTimer);
      controller?.abort();
    };
  }, []);

  return (
    <div className="grid gap-3">
      {readStatus && <Alert variant="destructive"><AlertDescription>{readStatus === "denied" ? "No tenés permiso para ver estas sesiones." : "No se pudo leer el estado de las sesiones. La tabla conserva la última respuesta disponible."}</AlertDescription></Alert>}
      {sessions.length === 0 ? (
        <p className="rounded-lg border p-4 text-sm text-muted-foreground">{readStatus ? "No hay una lectura disponible." : "No hay sesiones informadas."}</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead><tr className="border-b text-left"><th className="p-3">CUE</th><th className="p-3">Sesión</th><th className="p-3">Estado</th><th className="p-3">Alumnos</th><th className="p-3">En evaluación</th><th className="p-3">Entregados</th><th className="p-3">Cerrados/forzados</th><th className="p-3">Último heartbeat</th><th className="p-3">Señal</th></tr></thead>
            <tbody>{sessions.map(session => {
              const freshness = heartbeatLabel(session, now);
              const fresh = freshness === "Fresca";
              return <tr key={`${session.cue}-${session.sessionId}`} className="border-b last:border-0">
                <td className="p-3 font-mono">{session.cue || "—"}</td>
                <td className="p-3 font-mono">{session.sessionId}</td>
                <td className="p-3">{lifecycleLabel(session.status)}</td>
                <td className="p-3">{session.joinedCount}</td>
                <td className="p-3">{session.inProgressCount}</td>
                <td className="p-3">{session.submittedCount}</td>
                <td className="p-3">{session.closedOrForcedCount}</td>
                <td className="whitespace-nowrap p-3" title={session.lastHeartbeatAt ? dateFormatter.format(new Date(session.lastHeartbeatAt)) : undefined}>
                  {session.lastHeartbeatAt ? `${dateFormatter.format(new Date(session.lastHeartbeatAt))}${now === null ? "" : ` · ${formatHeartbeatAge(session.lastHeartbeatAt, now)}`}` : "Sin dato"}
                </td>
                <td className={`whitespace-nowrap p-3 ${fresh ? "text-emerald-700 dark:text-emerald-300" : "text-amber-700 dark:text-amber-300"}`}>{freshness}</td>
              </tr>;
            })}</tbody>
          </table>
        </div>
      )}
    </div>
  );
}
