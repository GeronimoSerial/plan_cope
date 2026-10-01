"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { activationNodeStatusLabel } from "./activation-key-mapping";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import { TableCell, TableRow } from "@/components/ui/table";
import type { ActivationKeyNode, ActivationKeySummary } from "../../_lib/api/server";

export function KeyDevicesRow({ keyRecord }: { keyRecord: ActivationKeySummary }) {
  const [nodes, setNodes] = useState<ActivationKeyNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [revokeTarget, setRevokeTarget] = useState<ActivationKeyNode | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    let active = true;
    void callCentral<ActivationKeyNode[]>(`admin/activation/keys/${encodeURIComponent(keyRecord.id)}/nodes`)
      .then(result => {
        if (active) setNodes(result);
      })
      .catch(error => toast.error(getErrorMessage(error, "No se pudieron cargar los equipos.")))
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [keyRecord.id]);

  async function revokeDevice() {
    if (!revokeTarget) return;
    setPending(true);
    try {
      await callCentral<undefined>(`admin/activation/nodes/${encodeURIComponent(revokeTarget.id)}/revoke`, {
        method: "POST",
        body: JSON.stringify({ reason: "Revocado desde la clave de activación" })
      });
      const revokedAt = new Date().toISOString();
      setNodes(current => current.map(node => node.id === revokeTarget.id ? { ...node, revokedAt } : node));
      toast.success("Equipo revocado.");
      setRevokeTarget(null);
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo revocar el equipo."));
    } finally {
      setPending(false);
    }
  }

  return (
    <>
      <TableRow>
        <TableCell colSpan={7} className="bg-muted/20 p-4">
          <div className="grid gap-3">
            <h3 className="text-sm font-medium">Equipos activados</h3>
            {loading ? <p className="text-sm text-muted-foreground">Cargando equipos…</p> : nodes.length === 0 ? (
              <p className="text-sm text-muted-foreground">Esta clave todavía no activó equipos.</p>
            ) : (
              <div className="grid gap-2">
                {nodes.map(node => (
                  <div key={node.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-background p-3 text-sm">
                    <div className="grid gap-1">
                      <span className="font-medium">{node.deviceName?.trim() || "Equipo sin nombre"} <span className="font-mono text-muted-foreground">({node.nodeCode})</span></span>
                      <span className="text-muted-foreground">
                        Alta {new Date(node.enrolledAt).toLocaleDateString("es-AR")} · Última conexión {node.lastSeenAt ? new Date(node.lastSeenAt).toLocaleString("es-AR") : "Sin conexión"}
                        {node.appVersion ? ` · Versión ${node.appVersion}` : ""} · {activationNodeStatusLabel(node.status, node.revokedAt)}
                      </span>
                    </div>
                    {node.revokedAt ? <span className="text-muted-foreground">Equipo revocado</span> : (
                      <Button size="sm" variant="destructive" onClick={() => setRevokeTarget(node)}>Revocar equipo</Button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </TableCell>
      </TableRow>
      <AlertDialog open={Boolean(revokeTarget)} onOpenChange={open => { if (!open && !pending) setRevokeTarget(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Revocar equipo</AlertDialogTitle>
            <AlertDialogDescription>
              El equipo {revokeTarget?.deviceName?.trim() || revokeTarget?.nodeCode} dejará de sincronizarse. La clave seguirá activa para otros equipos.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={pending} onClick={event => { event.preventDefault(); void revokeDevice(); }}>
              {pending ? "Revocando…" : "Revocar equipo"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
