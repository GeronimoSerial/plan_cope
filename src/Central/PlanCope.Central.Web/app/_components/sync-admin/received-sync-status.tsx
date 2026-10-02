"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { SidebarFooter } from "@/components/ui/sidebar";
import { callCentral, CentralApiError } from "../../_lib/api/client";
import type { ReceivedSyncPage } from "../../_lib/api/server";

const POLL_INTERVAL_MS = 60_000;
const dateFormatter = new Intl.DateTimeFormat("es-AR", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "America/Argentina/Buenos_Aires"
});

export function ReceivedSyncStatus() {
  const [status, setStatus] = useState<ReceivedSyncPage["inboxProcessing"] | null>(null);
  const [readStatus, setReadStatus] = useState<"denied" | "error" | null>(null);

  useEffect(() => {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let controller: AbortController | undefined;

    const poll = async () => {
      controller = new AbortController();
      try {
        const page = await callCentral<ReceivedSyncPage>("admin/sync/received?page=1&pageSize=1", { signal: controller.signal });
        if (!stopped) {
          setStatus(page.inboxProcessing);
          setReadStatus(null);
        }
      } catch (cause) {
        if (!stopped && !controller.signal.aborted) {
          setReadStatus(cause instanceof CentralApiError && cause.status === 403 ? "denied" : "error");
        }
      } finally {
        if (!stopped) timer = setTimeout(() => void poll(), POLL_INTERVAL_MS);
      }
    };

    void poll();
    return () => {
      stopped = true;
      if (timer) clearTimeout(timer);
      controller?.abort();
    };
  }, []);

  const receivedAt = status?.latestDurableReceivedAt;
  const processingLabel = status && (status.pending > 0 || status.failed > 0)
    ? `Posterior: ${status.pending} pendientes · ${status.failed} para reintentar`
    : status ? "Posterior: al día" : null;
  const tooltip = readStatus
    ? readStatus === "denied" ? "Sin permiso para consultar sincronización recibida" : "No se pudo leer sincronización recibida"
    : status
      ? `${receivedAt ? `Última recepción durable: ${dateFormatter.format(new Date(receivedAt))}` : "Sin recepciones durables"}. ${processingLabel}`
      : "Consultando sincronización recibida";

  return (
    <SidebarFooter className="border-t border-sidebar-border p-3 group-data-[collapsible=icon]:p-1">
      <Link href="/sincronizacion-recibida" title={tooltip} className="grid gap-1 rounded-md p-2 text-xs hover:bg-sidebar-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring group-data-[collapsible=icon]:justify-items-center" aria-label="Estado de sincronización recibida; abrir detalle">
        <span className="flex items-center gap-2 font-medium"><RefreshCw className="size-3.5" aria-hidden="true" /><span className="group-data-[collapsible=icon]:hidden">Sincronización recibida</span></span>
        {readStatus ? <span className="text-destructive group-data-[collapsible=icon]:hidden">{readStatus === "denied" ? "Sin permiso para consultar" : "No se pudo leer el estado"}</span> : !status ? <span className="text-muted-foreground group-data-[collapsible=icon]:hidden">Consultando…</span> : (
          <>
            <span className="text-muted-foreground group-data-[collapsible=icon]:hidden">{receivedAt ? `Última recepción durable: ${dateFormatter.format(new Date(receivedAt))}` : "Sin recepciones durables"}</span>
            <span className="text-muted-foreground group-data-[collapsible=icon]:hidden">{processingLabel}</span>
          </>
        )}
      </Link>
    </SidebarFooter>
  );
}
