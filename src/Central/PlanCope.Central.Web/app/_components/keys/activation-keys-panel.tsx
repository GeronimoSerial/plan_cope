"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { ConfirmDialog } from "../ui/confirm-dialog";
import { EmptyState } from "../ui/empty-state";
import { StatusBadge } from "../ui/status-badge";
import { TextField, TextAreaField } from "../ui/text-field";
import type { ActivationKeySummary } from "../../_lib/api/server";

interface IssueActivationKeyRequest {
  maxActivations: number;
  expiresAt?: string | null;
  note?: string | null;
}

interface IssueActivationKeyResponse {
  id: string;
  plaintextKey: string;
  keyPrefix: string;
  issuedAt: string;
  expiresAt?: string | null;
  maxActivations: number;
}

interface ActivationKeysPanelProps {
  initialKeys: ActivationKeySummary[];
}

const issueSchema = z.object({
  maxActivations: z.coerce.number().int().min(1, "La cantidad de activaciones debe ser al menos 1."),
  expiresAt: z.string().optional(),
  note: z.string().optional()
});

type IssueFormValues = z.input<typeof issueSchema>;
type IssueValues = z.output<typeof issueSchema>;

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

export function ActivationKeysPanel({ initialKeys }: ActivationKeysPanelProps) {
  const router = useRouter();
  const [keys, setKeys] = useState<ActivationKeySummary[]>(initialKeys);
  const [formOpen, setFormOpen] = useState(false);
  const [issueError, setIssueError] = useState<string | null>(null);
  const [issued, setIssued] = useState<IssueActivationKeyResponse | null>(null);
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ActivationKeySummary | null>(null);
  const [reason, setReason] = useState("");
  const [revokeDialogOpen, setRevokeDialogOpen] = useState(false);
  const [revoking, setRevoking] = useState(false);
  const [revokeError, setRevokeError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting }
  } = useForm<IssueFormValues, unknown, IssueValues>({
    resolver: zodResolver(issueSchema),
    defaultValues: { maxActivations: 0, expiresAt: "", note: "" }
  });

  async function refreshKeys() {
    try {
      const updated = await callCentral<ActivationKeySummary[]>("admin/activation/keys");
      setKeys(updated);
    } catch {
      router.refresh();
    }
  }

  async function onSubmit(values: IssueValues) {
    setIssueError(null);
    const payload: IssueActivationKeyRequest = {
      maxActivations: values.maxActivations,
      expiresAt: values.expiresAt ? new Date(values.expiresAt).toISOString() : null,
      note: values.note?.trim() ? values.note.trim() : null
    };

    try {
      const created = await callCentral<IssueActivationKeyResponse>("admin/activation/keys", {
        method: "POST",
        body: JSON.stringify(payload)
      });
      setIssued(created);
      setCopied(false);
      setCopyError(null);
      reset({ maxActivations: 0, expiresAt: "", note: "" });
    } catch (error) {
      setIssueError(getErrorMessage(error, "No se pudo emitir la clave."));
    }
  }

  async function copyPlaintextKey() {
    if (!issued) {
      return;
    }
    setCopyError(null);
    try {
      await navigator.clipboard.writeText(issued.plaintextKey);
      setCopied(true);
    } catch {
      setCopyError("No se pudo copiar automáticamente. Seleccioná la clave y copiala a mano.");
    }
  }

  async function dismissIssued() {
    setIssued(null);
    setCopied(false);
    setCopyError(null);
    await refreshKeys();
    router.refresh();
  }

  function startRevoke(key: ActivationKeySummary) {
    setRevokeTarget(key);
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
      await callCentral<undefined>(`admin/activation/keys/${encodeURIComponent(revokeTarget.id)}/revoke`, {
        method: "POST",
        body: JSON.stringify({ reason: reason.trim() })
      });
      setRevokeDialogOpen(false);
      setRevokeTarget(null);
      setReason("");
      await refreshKeys();
      router.refresh();
    } catch (error) {
      setRevokeError(getErrorMessage(error, "No se pudo revocar la clave."));
    } finally {
      setRevoking(false);
    }
  }

  return (
    <div className="stack">
      {issued && (
        <div className="card" style={{ borderColor: "var(--danger)" }}>
          <div className="card__body stack">
            <Banner tone="error">Esta clave no se va a volver a mostrar. Copiala ahora.</Banner>
            <div className="field">
              <label htmlFor="issued-activation-key">Clave de activación</label>
              <textarea
                id="issued-activation-key"
                readOnly
                rows={3}
                value={issued.plaintextKey}
                onFocus={event => event.currentTarget.select()}
                style={{ fontFamily: "monospace", fontSize: "15px", resize: "none" }}
              />
            </div>
            <div className="row">
              <Button onClick={() => void copyPlaintextKey()}>{copied ? "Copiada" : "Copiar clave"}</Button>
              <Button variant="secondary" onClick={() => void dismissIssued()}>
                Ya la copié, cerrar
              </Button>
            </div>
            {copyError && <Banner tone="error">{copyError}</Banner>}
          </div>
        </div>
      )}

      {revokeTarget && (
        <div className="card">
          <div className="card__body stack">
            <h3>Revocar clave {revokeTarget.keyPrefix}</h3>
            <p className="field__hint">La clave dejará de ser válida de inmediato. Esta acción no se puede deshacer.</p>
            <TextField
              label="Motivo (opcional)"
              placeholder="Ej. Clave comprometida"
              value={reason}
              onChange={event => setReason(event.target.value)}
            />
            <div className="row">
              <Button variant="danger" onClick={() => setRevokeDialogOpen(true)}>
                Revocar clave
              </Button>
              <Button variant="secondary" onClick={() => setRevokeTarget(null)}>
                Cancelar
              </Button>
            </div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card__header">
          <h2>Claves de activación</h2>
          <Button onClick={() => setFormOpen(open => !open)}>{formOpen ? "Cerrar" : "Nueva clave"}</Button>
        </div>
        <div className="card__body stack">
          {formOpen && (
            <form className="stack" onSubmit={handleSubmit(onSubmit)} noValidate>
              <div className="cols-2">
                <TextField
                  type="number"
                  min={1}
                  label="Activaciones máximas"
                  required
                  error={errors.maxActivations?.message}
                  {...register("maxActivations")}
                />
                <TextField
                  type="date"
                  label="Vencimiento (opcional)"
                  error={errors.expiresAt?.message}
                  {...register("expiresAt")}
                />
              </div>
              <TextAreaField
                label="Nota (opcional)"
                placeholder="Ej. Clave para la instalación de la sede Norte."
                error={errors.note?.message}
                {...register("note")}
              />
              <div className="row">
                <Button type="submit" disabled={isSubmitting}>
                  {isSubmitting ? "Emitiendo…" : "Emitir clave"}
                </Button>
              </div>
            </form>
          )}

          {issueError && <Banner tone="error">{issueError}</Banner>}
          {revokeError && <Banner tone="error">{revokeError}</Banner>}

          {keys.length === 0 ? (
            <EmptyState
              title="Aún no hay claves"
              description="Emití una clave de activación para comenzar."
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
                  <th style={{ padding: "0 0 var(--space-2)" }}>Clave</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Emitida</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Vencimiento</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Activaciones</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Estado</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {keys.map(key => (
                  <tr key={key.id} style={{ borderTop: "1px solid var(--line)" }}>
                    <td
                      style={{
                        padding: "var(--space-3) var(--space-1) var(--space-3) 0",
                        fontFamily: "monospace",
                        fontWeight: 700
                      }}
                    >
                      {key.keyPrefix}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{formatDate(key.issuedAt)}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      {key.expiresAt ? formatDate(key.expiresAt) : "Sin vencimiento"}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      {key.activationCount} / {key.maxActivations}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      <StatusBadge status={key.revokedAt ? "Revocada" : "Activa"} />
                    </td>
                    <td style={{ padding: "var(--space-3) 0 var(--space-3) var(--space-1)" }}>
                      {key.revokedAt ? (
                        <span className="field__hint">—</span>
                      ) : (
                        <Button variant="danger" size="sm" onClick={() => startRevoke(key)}>
                          Revocar
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
        title={revokeTarget ? `Revocar clave ${revokeTarget.keyPrefix}` : "Revocar clave"}
        description="La clave dejará de ser válida de inmediato. Esta acción no se puede deshacer. ¿Querés continuar?"
        confirmLabel="Sí, revocar"
        cancelLabel="Cancelar"
        busy={revoking}
        onConfirm={() => void confirmRevoke()}
        onCancel={() => setRevokeDialogOpen(false)}
      />
    </div>
  );
}
