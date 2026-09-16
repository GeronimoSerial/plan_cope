"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { ConfirmDialog } from "../ui/confirm-dialog";
import { EmptyState } from "../ui/empty-state";
import { StatusBadge } from "../ui/status-badge";
import { TextField } from "../ui/text-field";
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
  const [revokeDialogOpen, setRevokeDialogOpen] = useState(false);
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

  function cancelRevoke() {
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
      setRevokeDialogOpen(false);
      cancelRevoke();
      await refreshNodes();
      router.refresh();
    } catch (error) {
      setRevokeError(getErrorMessage(error, "No se pudo revocar el nodo."));
    } finally {
      setRevoking(false);
    }
  }

  return (
    <div className="stack">
      {revokeTarget && (
        <div className="card">
          <div className="card__body stack">
            <h3>Revocar nodo {revokeTarget.nodeCode}</h3>
            <p className="field__hint">
              Revocar un NODO es distinto de revocar una CLAVE: detiene únicamente esta máquina. La clave de
              activación que lo enroló sigue funcionando en otros equipos.
            </p>
            <p>
              Para confirmar, escribí el {revokeTarget.schoolName ? "nombre de la escuela" : "CUE"}{" "}
              <strong>{expectedConfirmation}</strong>.
            </p>
            <TextField
              label={
                revokeTarget.schoolName
                  ? "Escribí el nombre de la escuela para confirmar"
                  : "Escribí el CUE para confirmar"
              }
              value={typedConfirmation}
              onChange={event => setTypedConfirmation(event.target.value)}
              autoComplete="off"
              autoCapitalize="off"
              spellCheck={false}
            />
            <TextField
              label="Motivo (opcional)"
              placeholder="Ej. Equipo dado de baja"
              value={reason}
              onChange={event => setReason(event.target.value)}
            />
            <div className="row">
              <Button variant="danger" disabled={!confirmationMatches} onClick={() => setRevokeDialogOpen(true)}>
                Revocar nodo
              </Button>
              <Button variant="secondary" onClick={cancelRevoke}>
                Cancelar
              </Button>
            </div>
            {revokeError && <Banner tone="error">{revokeError}</Banner>}
          </div>
        </div>
      )}

      <div className="card">
        <div className="card__header">
          <h2>Nodos registrados</h2>
        </div>
        <div className="card__body stack">
          {nodes.length === 0 ? (
            <EmptyState
              title="Aún no hay nodos registrados"
              description="Los equipos aparecen acá a medida que se enrolan con una clave de activación."
            />
          ) : (
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr
                  style={{
                    textAlign: "left",
                    color: "var(--text-muted)",
                    fontSize: "12px",
                    textTransform: "uppercase",
                    letterSpacing: "0.05em"
                  }}
                >
                  <th style={{ padding: "0 0 var(--space-2)" }}>Nodo</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Escuela</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Equipo</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Registrado</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Última conexión</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Estado</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {nodes.map(node => (
                  <tr key={node.id} style={{ borderTop: "1px solid var(--line)" }}>
                    <td
                      style={{
                        padding: "var(--space-3) var(--space-1) var(--space-3) 0",
                        fontFamily: "monospace",
                        fontWeight: 700
                      }}
                    >
                      {node.nodeCode}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      {node.schoolName ?? `CUE ${node.cue}`}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{node.deviceName ?? "—"}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{formatDate(node.enrolledAt)}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      {node.lastSeenAt ? formatDate(node.lastSeenAt) : "Nunca"}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      <StatusBadge status={node.revokedAt ? "Revocado" : "Activo"} />
                    </td>
                    <td style={{ padding: "var(--space-3) 0 var(--space-3) var(--space-1)" }}>
                      {node.revokedAt ? (
                        <span className="field__hint">—</span>
                      ) : (
                        <Button variant="danger" size="sm" onClick={() => startRevoke(node)}>
                          Revocar nodo
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={revokeDialogOpen}
        title={revokeTarget ? `Revocar nodo ${revokeTarget.nodeCode}` : "Revocar nodo"}
        description="El nodo dejará de funcionar de inmediato. Esta acción es remota y no se puede deshacer. ¿Querés continuar?"
        confirmLabel="Sí, revocar"
        cancelLabel="Cancelar"
        busy={revoking}
        onConfirm={() => void confirmRevoke()}
        onCancel={() => setRevokeDialogOpen(false)}
      />
    </div>
  );
}
