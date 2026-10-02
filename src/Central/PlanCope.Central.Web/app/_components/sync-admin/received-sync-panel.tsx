"use client";

import { useState } from "react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import type { ReceivedSyncPage } from "../../_lib/api/server";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

interface ReceivedSyncPanelProps {
  initialPage: ReceivedSyncPage;
}

function statusLabel(value: string): string {
  if (value === "graded") return "Calificado";
  if (value === "ungradable") return "No calificable";
  if (value === "attributed") return "Atribuido";
  if (value === "unattributed") return "Sin atribuir";
  return "Pendiente";
}

function processingLabel(value: string): string {
  if (value === "complete") return "Completado";
  if (value === "retrying") return "Reintento pendiente";
  if (value === "needs_attention") return "Revisar";
  return "Pendiente";
}

function rollupLabel(value: string): string {
  if (value === "updated") return "Actualizado";
  if (value === "not_updated") return "Sin actualizar";
  return "Pendiente";
}

const receivedAtFormatter = new Intl.DateTimeFormat("es-AR", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "UTC"
});

export function ReceivedSyncPanel({ initialPage }: ReceivedSyncPanelProps) {
  const [data, setData] = useState(initialPage);
  const [loading, setLoading] = useState(false);
  const [reprocessing, setReprocessing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function loadPage(page: number) {
    setLoading(true);
    setError(null);
    try {
      setData(await callCentral<ReceivedSyncPage>(`admin/sync/received?page=${page}&pageSize=${data.pageSize}`));
    } catch (cause) {
      setError(getErrorMessage(cause, "No se pudieron cargar los intentos recibidos."));
    } finally {
      setLoading(false);
    }
  }

  async function reprocess() {
    setReprocessing(true);
    setError(null);
    setMessage(null);
    try {
      const result = await callCentral<{ reprocessed: number; rebuilt: number }>("admin/sync/received/reprocess", { method: "POST" });
      setMessage(`Reprocesamiento terminado: ${result.reprocessed} intentos revisados y ${result.rebuilt} rollups reconstruidos.`);
      await loadPage(data.page);
    } catch (cause) {
      setError(getErrorMessage(cause, "No se pudo reprocesar la sincronización recibida."));
    } finally {
      setReprocessing(false);
    }
  }

  const totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">{data.totalCount} intentos recibidos</p>
        <Button onClick={() => void reprocess()} disabled={reprocessing || loading}>
          {reprocessing ? "Reprocesando…" : "Reprocesar"}
        </Button>
      </div>
      {error && <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>}
      {message && <Alert><AlertDescription>{message}</AlertDescription></Alert>}
      {data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">Todavía no se recibieron intentos.</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Recepción durable</TableHead>
                <TableHead>Procesamiento posterior</TableHead>
                <TableHead>Nodo</TableHead>
                <TableHead>CUE</TableHead>
                <TableHead>Año lectivo</TableHead>
                <TableHead>Sección</TableHead>
                <TableHead>Versión</TableHead>
                <TableHead>Calificación</TableHead>
                <TableHead>Atribución</TableHead>
                <TableHead>Rollup</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map(item => (
                <TableRow key={item.attemptId}>
                  <TableCell className="whitespace-nowrap">
                    {item.receiptStatus === "durable" && item.durableReceivedAt
                      ? receivedAtFormatter.format(new Date(item.durableReceivedAt))
                      : "Sin confirmación durable"}
                  </TableCell>
                  <TableCell title={item.processingUpdatedAt ? `Actualizado: ${receivedAtFormatter.format(new Date(item.processingUpdatedAt))}` : undefined}>
                    {processingLabel(item.processingStatus)}
                  </TableCell>
                  <TableCell className="font-mono">{item.nodeId ?? "—"}</TableCell>
                  <TableCell className="font-mono">{item.cue ?? "—"}</TableCell>
                  <TableCell>{item.schoolYear ?? "—"}</TableCell>
                  <TableCell className="font-mono">{item.rosterSectionId ?? "—"}</TableCell>
                  <TableCell className="font-mono">{item.examVersionId ?? "—"}</TableCell>
                  <TableCell title={item.gradingReason ?? undefined}>{statusLabel(item.gradingStatus)}</TableCell>
                  <TableCell title={item.attributionReason ?? undefined}>{statusLabel(item.attributionStatus)}</TableCell>
                  <TableCell>{rollupLabel(item.rollupStatus)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      <div className="flex items-center justify-end gap-2">
        <Button variant="outline" disabled={loading || data.page <= 1} onClick={() => void loadPage(data.page - 1)}>Anterior</Button>
        <span className="text-sm text-muted-foreground">Página {data.page} de {totalPages}</span>
        <Button variant="outline" disabled={loading || data.page >= totalPages} onClick={() => void loadPage(data.page + 1)}>Siguiente</Button>
      </div>
    </div>
  );
}
