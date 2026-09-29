"use client";

import type { ExamDocument } from "../../_lib/schema/exam";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Textarea } from "@/components/ui/textarea";

interface ExamPreviewProps {
  document: ExamDocument;
}

// Vista previa de solo lectura: muestra el examen como lo veria el estudiante.
export function ExamPreview({ document }: ExamPreviewProps) {
  const meta = [document.subject, document.level, document.area].filter(Boolean).join(" · ");

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

        <ol className="grid gap-6">
          {document.questions.map((question, index) => (
            <li key={question.id} className="grid gap-2">
              <div className="flex items-baseline gap-2">
                <span className="text-sm font-semibold">{index + 1}.</span>
                <span className="text-sm font-medium">
                  {question.prompt || "(sin enunciado)"}
                  {"required" in question && question.required && <span className="text-destructive"> *</span>}
                </span>
              </div>
              {question.help && <p className="text-xs text-muted-foreground">{question.help}</p>}

              {(question.type === "single_choice" || question.type === "multiple_choice") && (
                <RadioGroup disabled className="gap-2">
                  {question.options.map(option => (
                    <div key={option.id} className="flex items-center gap-2">
                      {question.type === "single_choice" ? (
                        <RadioGroupItem value={option.id} disabled />
                      ) : (
                        <Checkbox disabled aria-label={option.label || "(opción vacía)"} />
                      )}
                      <Label className="font-normal">{option.label || "(opción vacía)"}</Label>
                    </div>
                  ))}
                </RadioGroup>
              )}

              {question.type === "true_false" && (
                <RadioGroup disabled className="grid-cols-2">
                  <div className="flex items-center gap-2">
                    <RadioGroupItem value="true" disabled />
                    <Label className="font-normal">Verdadero</Label>
                  </div>
                  <div className="flex items-center gap-2">
                    <RadioGroupItem value="false" disabled />
                    <Label className="font-normal">Falso</Label>
                  </div>
                </RadioGroup>
              )}

              {question.type === "free_text" && (
                <Textarea disabled placeholder="Respuesta del estudiante" maxLength={question.maxLength} />
              )}

              {question.type === "image_block" && (
                <p className="rounded-lg border border-dashed p-3 text-xs text-muted-foreground">
                  Imagen: {question.assetId || "(sin recurso)"}
                </p>
              )}
            </li>
          ))}
        </ol>
      </CardContent>
    </Card>
  );
}
