"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { StatusBadge } from "../ui/status-badge";
import { EmptyState } from "../ui/empty-state";
import type { ExamVersion } from "../../_lib/contracts";

interface VersionManagerProps {
  examId: string;
  initialVersions: ExamVersion[];
}

export function VersionManager({ examId, initialVersions }: VersionManagerProps) {
  const router = useRouter();
  const [versions] = useState<ExamVersion[]>(initialVersions);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function createVersion() {
    setCreating(true);
    setError(null);
    try {
      const created = await callCentral<ExamVersion>(`exams/${encodeURIComponent(examId)}/versions`, {
        method: "POST",
        body: JSON.stringify({ schemaVersion: 1, metadata: {} })
      });
      router.push(`/exams/${examId}/versions/${created.id}/builder`);
    } catch (caught) {
      setError(getErrorMessage(caught, "No se pudo crear la versión."));
      setCreating(false);
    }
  }

  return (
    <div className="card">
      <div className="card__header">
        <h2>Versiones</h2>
        <Button onClick={() => void createVersion()} disabled={creating}>
          {creating ? "Creando…" : "Nueva versión"}
        </Button>
      </div>
      <div className="card__body stack">
        {error && <Banner tone="error">{error}</Banner>}

        {versions.length === 0 ? (
          <EmptyState
            title="Sin versiones todavía"
            description="Creá una versión para comenzar."
          />
        ) : (
          <ul className="resource-list" style={{ listStyle: "none", padding: 0, margin: 0 }}>
            {versions.map(version => (
              <li key={version.id}>
                <Link href={`/exams/${examId}/versions/${version.id}/builder`} className="resource-card">
                  <div className="resource-card__title">
                    <span>Versión {version.versionNumber}</span>
                    <StatusBadge status={version.status} />
                  </div>
                  <div className="resource-card__meta">
                    {version.blocks?.length ?? 0} {(version.blocks?.length ?? 0) === 1 ? "pregunta" : "preguntas"}
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
