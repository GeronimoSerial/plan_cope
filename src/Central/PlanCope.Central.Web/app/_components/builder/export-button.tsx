"use client";

import { examDocumentSchema, type ExamDocument } from "../../_lib/schema/exam";
import { Button } from "../ui/button";

interface ExportButtonProps {
  document: ExamDocument;
  onError: (message: string) => void;
}

export function ExportButton({ document, onError }: ExportButtonProps) {
  function exportJson() {
    const result = examDocumentSchema.safeParse(document);
    if (!result.success) {
      onError("Corregí los errores del examen antes de exportar.");
      return;
    }

    const blob = new Blob([JSON.stringify(result.data, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = window.document.createElement("a");
    anchor.href = url;
    anchor.download = `${result.data.code || "examen"}.json`;
    window.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  return (
    <Button variant="secondary" onClick={exportJson}>
      Exportar JSON
    </Button>
  );
}
