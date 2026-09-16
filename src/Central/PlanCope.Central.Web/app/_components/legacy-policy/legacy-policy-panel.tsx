"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import {
  scoringPolicies,
  scoringPolicyExplanations,
  scoringPolicyLabels,
  scoringPolicyWarnings,
  type ScoringPolicy
} from "../../_lib/schema/exam";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { EmptyState } from "../ui/empty-state";
import { TextAreaField } from "../ui/text-field";
import type { UnassignedExamVersion } from "../../_lib/api/server";

interface LegacyPolicyPanelProps {
  initialVersions: UnassignedExamVersion[];
}

interface BulkAssignResponse {
  assignedCount: number;
  rejectedExamVersionIds: string[];
}

interface BulkAssignRequest {
  examVersionIds: string[];
  scoringPolicy: ScoringPolicy;
  note?: string | null;
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

export function LegacyPolicyPanel({ initialVersions }: LegacyPolicyPanelProps) {
  const router = useRouter();
  const [versions, setVersions] = useState<UnassignedExamVersion[]>(initialVersions);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [scoringPolicy, setScoringPolicy] = useState<ScoringPolicy | null>(null);
  const [note, setNote] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [assignedCount, setAssignedCount] = useState<number | null>(null);
  const [rejectedCount, setRejectedCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const selectAllRef = useRef<HTMLInputElement>(null);

  const allSelected = versions.length > 0 && selectedIds.length === versions.length;
  const someSelected = selectedIds.length > 0 && !allSelected;

  useEffect(() => {
    if (selectAllRef.current) {
      selectAllRef.current.indeterminate = someSelected;
    }
  }, [someSelected]);

  function toggleAll(checked: boolean) {
    setSelectedIds(checked ? versions.map(version => version.examVersionId) : []);
  }

  function toggleOne(examVersionId: string, checked: boolean) {
    setSelectedIds(current =>
      checked ? [...current, examVersionId] : current.filter(id => id !== examVersionId)
    );
  }

  async function refreshVersions() {
    try {
      const updated = await callCentral<UnassignedExamVersion[]>("admin/grading-policies/unassigned");
      setVersions(updated);
    } catch {
      router.refresh();
    }
  }

  async function onAssign() {
    if (selectedIds.length === 0 || scoringPolicy === null) {
      return;
    }
    setSubmitting(true);
    setError(null);
    setAssignedCount(null);
    setRejectedCount(0);
    try {
      const payload: BulkAssignRequest = {
        examVersionIds: selectedIds,
        scoringPolicy,
        note: note.trim() ? note.trim() : null
      };
      const result = await callCentral<BulkAssignResponse>("admin/grading-policies/bulk-assign", {
        method: "POST",
        body: JSON.stringify(payload)
      });
      setAssignedCount(result.assignedCount);
      setRejectedCount(result.rejectedExamVersionIds.length);
      setSelectedIds([]);
      setScoringPolicy(null);
      setNote("");
      await refreshVersions();
      router.refresh();
    } catch (assignError) {
      setError(getErrorMessage(assignError, "No se pudieron asignar las políticas."));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="stack">
      {assignedCount !== null && (
        <Banner tone="success">
          Se asignó la política a {assignedCount} {assignedCount === 1 ? "versión" : "versiones"}.
        </Banner>
      )}
      {rejectedCount > 0 && (
        <Banner tone="error">
          {rejectedCount} {rejectedCount === 1 ? "versión fue rechazada" : "versiones fueron rechazadas"}: ya
          tenían una política de puntaje asignada.
        </Banner>
      )}
      {error && <Banner tone="error">{error}</Banner>}

      <div className="card">
        <div className="card__header">
          <h2>Versiones sin política</h2>
        </div>
        <div className="card__body stack">
          {versions.length === 0 ? (
            <EmptyState
              title="No hay versiones publicadas sin política de puntaje."
              description="Cuando publiques una versión con bloques de opción múltiple sin regla asignada, va a aparecer acá."
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
                  <th style={{ padding: "0 0 var(--space-2)", width: "1%" }}>
                    <input
                      ref={selectAllRef}
                      type="checkbox"
                      aria-label="Seleccionar todas las versiones"
                      checked={allSelected}
                      onChange={event => toggleAll(event.target.checked)}
                      style={{ width: "auto" }}
                    />
                  </th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Examen</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Versión</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Publicada</th>
                </tr>
              </thead>
              <tbody>
                {versions.map(version => {
                  const selected = selectedIds.includes(version.examVersionId);
                  return (
                    <tr key={version.examVersionId} style={{ borderTop: "1px solid var(--line)" }}>
                      <td style={{ padding: "var(--space-3) var(--space-1) var(--space-3) 0" }}>
                        <input
                          type="checkbox"
                          aria-label={`Seleccionar ${version.examCode} versión ${version.versionNumber}`}
                          checked={selected}
                          onChange={event => toggleOne(version.examVersionId, event.target.checked)}
                          style={{ width: "auto" }}
                        />
                      </td>
                      <td
                        style={{
                          padding: "var(--space-3) var(--space-1)",
                          fontFamily: "monospace",
                          fontWeight: 700
                        }}
                      >
                        {version.examCode}
                      </td>
                      <td style={{ padding: "var(--space-3) var(--space-1)" }}>{version.versionNumber}</td>
                      <td style={{ padding: "var(--space-3) 0 var(--space-3) var(--space-1)" }}>
                        {version.publishedAt ? formatDate(version.publishedAt) : "—"}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <h2>Política de puntaje</h2>
        </div>
        <div className="card__body stack">
          <div className="field">
            <label id="legacy-scoring-policy-label">Política a asignar</label>
            <span className="field__hint">
              Elegí explícitamente una regla. No hay una opción seleccionada por defecto.
            </span>
            <div className="stack" role="radiogroup" aria-labelledby="legacy-scoring-policy-label">
              {scoringPolicies.map(policy => (
                <div key={policy} className="stack">
                  <label
                    htmlFor={`legacy-scoring-policy-${policy}`}
                    style={{ display: "flex", alignItems: "flex-start", gap: "var(--space-2)", fontWeight: 400 }}
                  >
                    <input
                      id={`legacy-scoring-policy-${policy}`}
                      type="radio"
                      name="legacy-scoring-policy"
                      checked={scoringPolicy === policy}
                      onChange={() => setScoringPolicy(policy)}
                      style={{ width: "auto", marginTop: 3 }}
                    />
                    <span>{scoringPolicyLabels[policy]}</span>
                  </label>
                  <span
                    className="field__hint"
                    style={{ display: "block", marginLeft: "calc(var(--space-2) + 16px)" }}
                  >
                    {scoringPolicyExplanations[policy]}
                  </span>
                  {scoringPolicy === policy && scoringPolicyWarnings[policy] && (
                    <Banner tone="error">{scoringPolicyWarnings[policy]}</Banner>
                  )}
                </div>
              ))}
            </div>
          </div>

          <TextAreaField
            label="Nota (opcional)"
            placeholder="Ej. Migración de exámenes publicados antes de la regla."
            value={note}
            onChange={event => setNote(event.target.value)}
          />

          <div className="row">
            <Button
              onClick={() => void onAssign()}
              disabled={selectedIds.length === 0 || scoringPolicy === null || submitting}
            >
              {submitting ? "Asignando…" : "Asignar"}
            </Button>
            <span className="field__hint">
              {selectedIds.length === 0
                ? "Seleccioná al menos una versión."
                : `${selectedIds.length} ${selectedIds.length === 1 ? "versión seleccionada" : "versiones seleccionadas"}.`}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
