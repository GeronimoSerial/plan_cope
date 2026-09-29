"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { RegisteredNodeSummary } from "../../_lib/api/server";

interface NodeRegistryPanelProps {
  initialNodes: RegisteredNodeSummary[];
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

export function NodeRegistryPanel({ initialNodes }: NodeRegistryPanelProps) {
  const router = useRouter();
  const [nodes, setNodes] = useState<RegisteredNodeSummary[]>(initialNodes);
  const [revokeTarget, setRevokeTarget] = useState<RegisteredNodeSummary | null>(null);
  const [typedConfirmation, setTypedConfirmation] = useState("");
  const [reason, setReason] = useState("");
  const [revoking, setRevoking] = useState(false);
  const [revokeError, setRevokeError] = useState<string | null>(null);

  const expectedConfirmation = revokeTarget ? (revokeTarget.schoolName ?? revokeTarget.cue) : "";
  const confirmationMatches = revokeTarget !== null && typedConfirmation.trim() === expectedConfirmation.trim();

  async function refreshNodes() {
    try {
      const updated = await callCentral<RegisteredNodeSummary[]>("admin/activation/nodes");
      setNodes(updated);
    } catch {
      router.refresh();
    }
  }

  function startRevoke(node: RegisteredNodeSummary) {
    setRevokeTarget(node);
    setTypedConfirmation("");
    setReason("");
    setRevokeError(null);
  }

  function closeRevoke() {
    if (revoking) {
      return;
    }
    setRevokeTarget(null);
    setTypedConfirmation("");
    setReason("");
    setRevokeError(null);
  }

  async function confirmRevoke() {
    if (!revokeTarget) {
      return;
    }
    setRevoking(true);
    setRevokeError(null);
    try {
      await callCentral<undefined>(`admin/activation/nodes/${encodeURIComponent(revokeTarget.id)}/revoke`, {
        method: "POST",
        body: JSON.stringify({ reason: reason.trim() })
      });
      setRevoking(false);
      closeRevoke();
      await refreshNodes();
      router.refresh();
    } catch (error) {
      setRevokeError(getErrorMessage(error, "No se pudo revocar el nodo."));
      setRevoking(false);
    }
  }

  return (
    <>
      {nodes.length === 0 ? (
        <p className="text-sm text-muted-foreground">No hay nodos registrados.</p>
      ) : (
        <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nodo</TableHead>
                <TableHead>Escuela</TableHead>
                <TableHead>Equipo</TableHead>
                <TableHead>Registrado</TableHead>
                <TableHead>Última conexión</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {nodes.map(node => (
                <TableRow key={node.id}>
                  <TableCell className="font-mono font-medium">{node.nodeCode}</TableCell>
                  <TableCell>{node.schoolName ?? `CUE ${node.cue}`}</TableCell>
                  <TableCell>{node.deviceName ?? "—"}</TableCell>
                  <TableCell>{formatDate(node.enrolledAt)}</TableCell>
                  <TableCell>{node.lastSeenAt ? formatDate(node.lastSeenAt) : "Nunca"}</TableCell>
                  <TableCell>
                    <Badge variant={node.revokedAt ? "destructive" : "secondary"}>
                      {node.revokedAt ? "Revocado" : "Activo"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    {node.revokedAt ? (
                      <span className="text-muted-foreground">—</span>
                    ) : (
                      <Button variant="destructive" size="sm" onClick={() => startRevoke(node)}>
                        Revocar nodo
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <Dialog
        open={revokeTarget !== null}
        onOpenChange={open => {
          if (!open) {
            closeRevoke();
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Revocar nodo {revokeTarget?.nodeCode}</DialogTitle>
            <DialogDescription>
              Revocar un nodo detiene únicamente esta máquina. La clave que lo enroló sigue funcionando en otros
              equipos.
            </DialogDescription>
          </DialogHeader>

          <div className="grid gap-4">
            <p className="text-sm">
              Para confirmar, escribí el {revokeTarget?.schoolName ? "nombre de la escuela" : "CUE"}{" "}
              <span className="font-medium">{expectedConfirmation}</span>.
            </p>
            <div className="grid gap-1.5">
              <Label htmlFor="revoke-confirm">
                {revokeTarget?.schoolName ? "Nombre de la escuela" : "CUE"}
              </Label>
              <Input
                id="revoke-confirm"
                value={typedConfirmation}
                onChange={event => setTypedConfirmation(event.target.value)}
                autoComplete="off"
                autoCapitalize="off"
                spellCheck={false}
              />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="revoke-reason">Motivo (opcional)</Label>
              <Input
                id="revoke-reason"
                value={reason}
                onChange={event => setReason(event.target.value)}
                placeholder="Ej. Equipo dado de baja"
              />
            </div>
            {revokeError && (
              <Alert variant="destructive">
                <AlertDescription>{revokeError}</AlertDescription>
              </Alert>
            )}
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={closeRevoke} disabled={revoking}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              disabled={!confirmationMatches || revoking}
              onClick={() => void confirmRevoke()}
            >
              {revoking ? "Revocando…" : "Revocar nodo"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
