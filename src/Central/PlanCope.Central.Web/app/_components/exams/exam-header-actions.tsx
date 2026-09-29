"use client";

import { useState } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { CreateVersionDialog } from "./create-version-dialog";
import { EditExamButton } from "./edit-exam-dialog";
import { primaryEditTarget } from "../../_lib/exams/version-state";
import type { ExamSummary, ExamVersion } from "../../_lib/contracts";

interface ExamHeaderActionsProps {
  exam: ExamSummary;
  versions: ExamVersion[];
  canEditExams: boolean;
}

// Acciones principales del detalle del examen: editar los datos generales y "Editar", que abre el
// borrador si existe o crea una copia de la version publicada actual si no.
export function ExamHeaderActions({ exam, versions, canEditExams }: ExamHeaderActionsProps) {
  const [createOpen, setCreateOpen] = useState(false);
  const target = primaryEditTarget(versions);

  if (target.kind === "open-draft") {
    return (
      <div className="flex flex-wrap items-center gap-2">
        <EditExamButton exam={exam} canEditExams={canEditExams} />
        {canEditExams && <Button nativeButton={false} render={<Link href={`/exams/${exam.id}/versions/${target.versionId}/builder`} />}>Editar</Button>}
      </div>
    );
  }

  if (target.kind === "create-from") {
    return (
      <div className="flex flex-wrap items-center gap-2">
        <EditExamButton exam={exam} canEditExams={canEditExams} />
        {canEditExams && <Button onClick={() => setCreateOpen(true)}>Editar</Button>}
        {canEditExams && <CreateVersionDialog
          examId={exam.id}
          open={createOpen}
          onOpenChange={setCreateOpen}
          sourceVersionId={target.sourceVersionId}
          sourceNumber={target.sourceNumber}
          sourcePublished
          nextNumber={target.nextNumber}
        />}
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <EditExamButton exam={exam} canEditExams={canEditExams} />
    </div>
  );
}
