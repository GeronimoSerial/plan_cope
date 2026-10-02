"use client";

import type { ExamDocument } from "../../_lib/schema/exam";
import { courseLabel } from "../../_lib/exams/catalog";
import { Card, CardContent } from "@/components/ui/card";
import { QuestionPreview } from "./question-preview";

interface ExamPreviewProps {
  document: ExamDocument;
  versionId: string;
}

// Read-only preview showing the exam as a student will see it.
export function ExamPreview({ document, versionId }: ExamPreviewProps) {
  const grades = (document.courses ?? []).map(courseLabel).join(", ");
  const meta = [document.subject, grades ? `Grados: ${grades}` : "", document.area].filter(Boolean).join(" · ");

  return (
    <Card>
      <CardContent className="grid gap-6">
        <header className="grid gap-1">
          <h2 className="text-lg font-semibold">{document.title || "Examen sin título"}</h2>
          {document.description && <p className="text-sm text-muted-foreground">{document.description}</p>}
          {meta && <p className="text-xs text-muted-foreground">{meta}</p>}
        </header>

        {document.questions.length === 0 && (
          <p className="text-sm text-muted-foreground">Sin preguntas para previsualizar.</p>
        )}

        <ol className="grid gap-4">
          {document.questions.map((question, index) => (
            <li key={question.id}>
              <QuestionPreview question={question} versionId={versionId} number={index + 1} />
            </li>
          ))}
        </ol>
      </CardContent>
    </Card>
  );
}
