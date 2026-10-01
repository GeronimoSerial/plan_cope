"use client";

import { useId, useState } from "react";
import { XIcon } from "lucide-react";
import {
  questionTypes,
  questionTypeLabels,
  scoringPolicies,
  scoringPolicyExplanations,
  scoringPolicyLabels,
  scoringPolicyWarnings,
  type Question,
  type QuestionType,
  type ExamOption,
  type ScoringPolicy
} from "../../_lib/schema/exam";
import { blankQuestion, newId } from "../../_lib/schema/mappers";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Field, FieldError, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { TermHint } from "../help/term-hint";
import { callCentral } from "../../_lib/api/client";

const MAX_IMAGE_SIZE = 2 * 1024 * 1024;
const ALLOWED_IMAGE_TYPES = new Set(["image/jpeg", "image/png", "image/webp"]);

function encodeBase64(bytes: Uint8Array): string {
  let binary = "";
  const chunkSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
  }
  return btoa(binary);
}

interface QuestionEditorProps {
  question: Question;
  versionId: string;
  errors: Record<string, string>;
  disabled?: boolean;
  onChange: (next: Question) => void;
}

export function QuestionEditor({ question, versionId, errors, disabled = false, onChange }: QuestionEditorProps) {
  const uid = useId();
  const [uploadingImage, setUploadingImage] = useState(false);
  const [imageError, setImageError] = useState<string | null>(null);

  function patchCommon(patch: Partial<{ prompt: string; help: string | undefined; required: boolean; score: number }>) {
    onChange({ ...question, ...patch } as Question);
  }

  function changeType(type: QuestionType) {
    if (type === question.type) {
      return;
    }
    // Keep the prompt, help, score, and required setting; reset fields specific to the type.
    const fresh = blankQuestion(type);
    onChange({
      ...fresh,
      id: question.id,
      prompt: question.prompt ?? "",
      help: question.help,
      ...("required" in question ? { required: question.required } : {}),
      ...("score" in question ? { score: question.score } : {}),
      ...(question.imageAssetId ? { imageAssetId: question.imageAssetId } : {})
    } as Question);
  }

  async function uploadImage(file: File | undefined, input: HTMLInputElement) {
    setImageError(null);
    if (!file) return;
    if (!ALLOWED_IMAGE_TYPES.has(file.type)) {
      setImageError("Elegí una imagen JPEG, PNG o WebP.");
      input.value = "";
      return;
    }
    if (file.size > MAX_IMAGE_SIZE) {
      setImageError("La imagen no puede superar los 2 MB.");
      input.value = "";
      return;
    }

    setUploadingImage(true);
    try {
      const contentBase64 = encodeBase64(new Uint8Array(await file.arrayBuffer()));
      const asset = await callCentral<{ id: string }>(`exams/versions/${encodeURIComponent(versionId)}/assets`, {
        method: "POST",
        body: JSON.stringify({ fileName: file.name, mimeType: file.type, contentBase64 })
      });
      onChange({ ...question, imageAssetId: asset.id } as Question);
    } catch (error) {
      setImageError(error instanceof Error ? error.message : "No se pudo subir la imagen.");
    } finally {
      setUploadingImage(false);
      input.value = "";
    }
  }

  const promptLabel = "Enunciado";

  return (
    <div className="grid gap-4">
      <Field>
        <FieldLabel htmlFor={`${uid}-type`}>Tipo</FieldLabel>
        <Select
          value={question.type}
          onValueChange={value => changeType(value as QuestionType)}
          disabled={disabled}
          items={questionTypeLabels}
        >
          <SelectTrigger id={`${uid}-type`} className="w-full" aria-label="Tipo de pregunta">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {questionTypes.map(type => (
              <SelectItem key={type} value={type}>
                {questionTypeLabels[type]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>

      <Field data-invalid={errors.prompt ? true : undefined}>
        <FieldLabel htmlFor={`${uid}-prompt`}>{promptLabel}</FieldLabel>
        <Textarea
          id={`${uid}-prompt`}
          value={question.prompt ?? ""}
          disabled={disabled}
          onChange={event => patchCommon({ prompt: event.target.value })}
          aria-invalid={errors.prompt ? true : undefined}
          placeholder="Escribí la pregunta tal como la verá el estudiante."
        />
        {errors.prompt && <FieldError>{errors.prompt}</FieldError>}
      </Field>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel htmlFor={`${uid}-help`}>Texto de ayuda (opcional)</FieldLabel>
          <Input
            id={`${uid}-help`}
            value={question.help ?? ""}
            disabled={disabled}
            onChange={event => patchCommon({ help: event.target.value || undefined })}
            placeholder="Aclaración opcional."
          />
        </Field>
        <Field>
          <div className="flex items-center gap-1.5">
            <FieldLabel htmlFor={`${uid}-score`}>Puntos de esta pregunta</FieldLabel>
            <TermHint term="puntos" />
          </div>
          <Input
            id={`${uid}-score`}
            type="number"
            min={0}
            step={1}
            value={question.score}
            disabled={disabled}
            onChange={event => patchCommon({ score: Number(event.target.value) })}
          />
        </Field>
      </div>

      <div className="flex items-center gap-2">
        <Checkbox
          id={`${uid}-required`}
          checked={question.required}
          disabled={disabled}
          onCheckedChange={checked => patchCommon({ required: checked === true })}
        />
        <Label htmlFor={`${uid}-required`} className="font-normal">
          Respuesta obligatoria
        </Label>
      </div>

      <Field data-invalid={imageError ? true : undefined}>
        <FieldLabel htmlFor={`${uid}-image`}>Imagen (opcional)</FieldLabel>
        {question.imageAssetId ? (
          <div className="grid justify-items-start gap-2">
            <img
              src={`/api/central/exams/versions/${encodeURIComponent(versionId)}/assets/${encodeURIComponent(question.imageAssetId)}`}
              alt={question.prompt}
              className="max-h-48 max-w-full rounded-md border object-contain"
            />
            <Button type="button" variant="outline" size="sm" disabled={disabled || uploadingImage} onClick={() => {
              setImageError(null);
              onChange({ ...question, imageAssetId: undefined } as Question);
            }}>
              Quitar
            </Button>
          </div>
        ) : (
          <Input
            id={`${uid}-image`}
            type="file"
            accept=".jpg,.jpeg,.png,.webp"
            disabled={disabled || uploadingImage}
            aria-label="Agregar imagen"
            onChange={event => void uploadImage(event.currentTarget.files?.[0], event.currentTarget)}
          />
        )}
        {uploadingImage && <p className="text-sm text-muted-foreground">Subiendo imagen…</p>}
        {imageError && <FieldError>{imageError}</FieldError>}
      </Field>

      {(question.type === "single_choice" || question.type === "multiple_choice") && (
        <ChoiceEditor question={question} errors={errors} disabled={disabled} onChange={onChange} />
      )}
      {question.type === "true_false" && (
        <Field>
          <FieldLabel>Respuesta correcta</FieldLabel>
          <RadioGroup
            value={question.correctAnswer ? "true" : "false"}
            onValueChange={value => onChange({ ...question, correctAnswer: value === "true" })}
            disabled={disabled}
            className="grid-cols-2"
          >
            <div className="flex items-center gap-2">
              <RadioGroupItem id={`${uid}-tf-true`} value="true" disabled={disabled} />
              <Label htmlFor={`${uid}-tf-true`} className="font-normal">
                Verdadero
              </Label>
            </div>
            <div className="flex items-center gap-2">
              <RadioGroupItem id={`${uid}-tf-false`} value="false" disabled={disabled} />
              <Label htmlFor={`${uid}-tf-false`} className="font-normal">
                Falso
              </Label>
            </div>
          </RadioGroup>
        </Field>
      )}

    </div>
  );
}

function ChoiceEditor({
  question,
  errors,
  disabled,
  onChange
}: {
  question: Extract<Question, { type: "single_choice" | "multiple_choice" }>;
  errors: Record<string, string>;
  disabled: boolean;
  onChange: (next: Question) => void;
}) {
  const single = question.type === "single_choice";

  function updateOptions(options: ExamOption[]) {
    onChange({ ...question, options });
  }

  function setLabel(id: string, label: string) {
    updateOptions(question.options.map(option => (option.id === id ? { ...option, label } : option)));
  }

  function toggleCorrect(id: string) {
    if (single) {
      updateOptions(question.options.map(option => ({ ...option, isCorrect: option.id === id })));
    } else {
      updateOptions(question.options.map(option => (option.id === id ? { ...option, isCorrect: !option.isCorrect } : option)));
    }
  }

  function addOption() {
    updateOptions([...question.options, { id: newId(), label: "", isCorrect: false }]);
  }

  function removeOption(id: string) {
    if (question.options.length <= 2) {
      return;
    }
    updateOptions(question.options.filter(option => option.id !== id));
  }

  return (
    <div className="grid gap-4">
      {!single && (
        <Field>
          <FieldLabel htmlFor={`scoring-policy-${question.id}`}>Regla de puntaje</FieldLabel>
          <Select
            value={question.scoringPolicy ?? "AllOrNothing"}
            onValueChange={value => onChange({ ...question, scoringPolicy: value as ScoringPolicy })}
            disabled={disabled}
            items={scoringPolicyLabels}
          >
            <SelectTrigger id={`scoring-policy-${question.id}`} className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {scoringPolicies.map(policy => (
                <SelectItem key={policy} value={policy}>
                  {scoringPolicyLabels[policy]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <p className="text-xs text-muted-foreground">
            {scoringPolicyExplanations[question.scoringPolicy ?? "AllOrNothing"]}
          </p>
          {scoringPolicyWarnings[question.scoringPolicy ?? "AllOrNothing"] && (
            <p className="text-xs text-muted-foreground">
              {scoringPolicyWarnings[question.scoringPolicy ?? "AllOrNothing"]}
            </p>
          )}
        </Field>
      )}
      <Field data-invalid={errors.options ? true : undefined}>
        <FieldLabel>Opciones {single ? "(marcá la correcta)" : "(marcá todas las correctas)"}</FieldLabel>
        <div className="grid gap-2">
          {question.options.map((option, index) => (
            <div key={option.id} className="flex items-center gap-2">
              <Checkbox
                checked={option.isCorrect}
                disabled={disabled}
                onCheckedChange={() => toggleCorrect(option.id)}
                aria-label={`Marcar opción ${index + 1} como correcta`}
              />
              <Input
                value={option.label}
                disabled={disabled}
                onChange={event => setLabel(option.id, event.target.value)}
                placeholder={`Opción ${index + 1}`}
                aria-label={`Texto de la opción ${index + 1}`}
              />
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                disabled={disabled || question.options.length <= 2}
                onClick={() => removeOption(option.id)}
                aria-label={`Eliminar opción ${index + 1}`}
              >
                <XIcon />
              </Button>
            </div>
          ))}
        </div>
        {errors.options && <FieldError>{errors.options}</FieldError>}
        <div>
          <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={addOption}>
            Agregar opción
          </Button>
        </div>
      </Field>
    </div>
  );
}
