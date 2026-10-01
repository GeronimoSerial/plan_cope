"use client";

import { useEffect, useId, useRef, useState } from "react";
import Image from "next/image";
import { XIcon } from "lucide-react";
import {
  scoringPolicies,
  scoringPolicyExplanations,
  scoringPolicyLabels,
  scoringPolicyWarnings,
  type Question,
  type ExamOption,
  type ScoringPolicy
} from "../../_lib/schema/exam";
import { newId } from "../../_lib/schema/mappers";
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
  const promptRef = useRef<HTMLTextAreaElement>(null);
  const imageInputRef = useRef<HTMLInputElement>(null);
  const [uploadingImage, setUploadingImage] = useState(false);
  const [imageError, setImageError] = useState<string | null>(null);
  const [moreOptionsOpen, setMoreOptionsOpen] = useState(Boolean(question.help?.trim()));

  useEffect(() => {
    const textarea = promptRef.current;
    if (textarea) {
      textarea.style.height = "auto";
      textarea.style.height = `${textarea.scrollHeight}px`;
    }
  }, [question.prompt]);

  function patchCommon(patch: Partial<{ prompt: string; help: string | undefined; required: boolean; score: number }>) {
    onChange({ ...question, ...patch } as Question);
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

  return (
    <div className="grid gap-2.5">
      <div className="grid grid-cols-1 items-start gap-2 min-[640px]:grid-cols-[minmax(0,1fr)_auto]">
        <Field data-invalid={errors.prompt ? true : undefined} className="gap-1">
          <FieldLabel htmlFor={`${uid}-prompt`}>Enunciado</FieldLabel>
          <Textarea
            ref={promptRef}
            id={`${uid}-prompt`}
            rows={2}
            className="min-h-16 resize-none overflow-hidden"
            value={question.prompt ?? ""}
            disabled={disabled}
            onChange={event => patchCommon({ prompt: event.target.value })}
            aria-invalid={errors.prompt ? true : undefined}
            placeholder="Escribí la pregunta tal como la verá el estudiante."
          />
          {errors.prompt && <FieldError>{errors.prompt}</FieldError>}
        </Field>

        <Field data-invalid={imageError ? true : undefined} className="gap-1 min-[640px]:pt-6">
          {question.imageAssetId ? (
            <div className="flex items-center gap-2">
              <Image
                src={`/api/central/exams/versions/${encodeURIComponent(versionId)}/assets/${encodeURIComponent(question.imageAssetId)}`}
                alt={question.prompt || "Imagen de la pregunta"}
                width={80}
                height={56}
                unoptimized
                className="h-14 w-20 rounded-md border object-contain"
              />
              <Button type="button" variant="outline" size="sm" disabled={disabled || uploadingImage} onClick={() => {
                setImageError(null);
                onChange({ ...question, imageAssetId: undefined } as Question);
              }}>Quitar</Button>
            </div>
          ) : (
            <>
              <input
                ref={imageInputRef}
                id={`${uid}-image`}
                type="file"
                accept=".jpg,.jpeg,.png,.webp"
                className="sr-only"
                tabIndex={-1}
                disabled={disabled || uploadingImage}
                aria-label="Agregar imagen"
                onChange={event => void uploadImage(event.currentTarget.files?.[0], event.currentTarget)}
              />
              <Button type="button" variant="outline" size="sm" className="w-fit" disabled={disabled || uploadingImage} onClick={() => imageInputRef.current?.click()}>
                Agregar imagen
              </Button>
            </>
          )}
          {uploadingImage && <p className="text-xs text-muted-foreground">Subiendo imagen…</p>}
          {imageError && <FieldError>{imageError}</FieldError>}
        </Field>
      </div>

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
            className="flex flex-wrap gap-x-5 gap-y-1"
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
      <details
        className="border-t pt-2"
        open={moreOptionsOpen}
        onToggle={event => setMoreOptionsOpen(event.currentTarget.open)}
      >
        <summary className="w-fit cursor-pointer text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">Más opciones</summary>
        <div className="grid gap-2 pt-2 sm:grid-cols-2">
          <Field className={question.type === "multiple_choice" ? "sm:col-span-2" : undefined}>
            <FieldLabel htmlFor={`${uid}-help`}>Texto de ayuda (opcional)</FieldLabel>
            <Input
              id={`${uid}-help`}
              value={question.help ?? ""}
              disabled={disabled}
              onChange={event => {
                patchCommon({ help: event.target.value || undefined });
                if (event.target.value.trim()) setMoreOptionsOpen(true);
              }}
              placeholder="Aclaración opcional."
            />
          </Field>
          {question.type === "multiple_choice" && (
            <Field className="sm:col-span-2">
              <div className="flex items-center gap-1.5">
                <FieldLabel htmlFor={`scoring-policy-${question.id}`}>Regla de puntaje</FieldLabel>
                <TermHint term="puntos" />
              </div>
              <Select
                value={question.scoringPolicy ?? "AllOrNothing"}
                onValueChange={value => onChange({ ...question, scoringPolicy: value as ScoringPolicy })}
                disabled={disabled}
                items={scoringPolicyLabels}
              >
                <SelectTrigger id={`scoring-policy-${question.id}`} className="h-9 w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {scoringPolicies.map(policy => <SelectItem key={policy} value={policy}>{scoringPolicyLabels[policy]}</SelectItem>)}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">{scoringPolicyExplanations[question.scoringPolicy ?? "AllOrNothing"]}</p>
              {scoringPolicyWarnings[question.scoringPolicy ?? "AllOrNothing"] && <p className="text-xs text-muted-foreground">{scoringPolicyWarnings[question.scoringPolicy ?? "AllOrNothing"]}</p>}
            </Field>
          )}
        </div>
      </details>
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
    <div className="grid gap-2">
      <Field data-invalid={errors.options ? true : undefined}>
        <FieldLabel>Opciones {single ? "(marcá la correcta)" : "(marcá todas las correctas)"}</FieldLabel>
        <div className="grid gap-2">
          {single ? (
            <RadioGroup
              value={question.options.find(option => option.isCorrect)?.id ?? ""}
              onValueChange={toggleCorrect}
              disabled={disabled}
              className="grid gap-2"
            >
              {question.options.map((option, index) => (
                <OptionRow key={option.id} option={option} index={index} disabled={disabled} canRemove={question.options.length > 2} onLabelChange={setLabel} onRemove={removeOption}>
                  <RadioGroupItem id={`correct-${question.id}-${option.id}`} value={option.id} aria-label={`Marcar opción ${index + 1} como correcta`} />
                </OptionRow>
              ))}
            </RadioGroup>
          ) : question.options.map((option, index) => (
            <OptionRow key={option.id} option={option} index={index} disabled={disabled} canRemove={question.options.length > 2} onLabelChange={setLabel} onRemove={removeOption}>
              <Checkbox checked={option.isCorrect} disabled={disabled} onCheckedChange={() => toggleCorrect(option.id)} aria-label={`Marcar opción ${index + 1} como correcta`} />
            </OptionRow>
          ))}
        </div>
        {errors.options && <FieldError>{errors.options}</FieldError>}
        <div>
          <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={addOption}>
            + Agregar opción
          </Button>
        </div>
      </Field>
    </div>
  );
}

function OptionRow({
  option,
  index,
  disabled,
  canRemove,
  onLabelChange,
  onRemove,
  children
}: {
  option: ExamOption;
  index: number;
  disabled: boolean;
  canRemove: boolean;
  onLabelChange: (id: string, label: string) => void;
  onRemove: (id: string) => void;
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-w-0 items-center gap-2">
      {children}
      <Input
        className="min-w-0 flex-1"
        value={option.label}
        disabled={disabled}
        onChange={event => onLabelChange(option.id, event.target.value)}
        placeholder={`Opción ${index + 1}`}
        aria-label={`Texto de la opción ${index + 1}`}
      />
      <Button type="button" variant="ghost" size="icon-sm" disabled={disabled || !canRemove} onClick={() => onRemove(option.id)} aria-label={`Eliminar opción ${index + 1}`} title={`Eliminar opción ${index + 1}`}>
        <XIcon />
      </Button>
    </div>
  );
}
