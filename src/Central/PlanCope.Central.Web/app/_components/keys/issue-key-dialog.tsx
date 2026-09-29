"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Check, Copy } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";

export interface IssuedActivationKey {
  id: string;
  plaintextKey: string;
  keyPrefix: string;
  issuedAt: string;
  expiresAt?: string | null;
  maxActivations: number;
}

const issueSchema = z.object({
  maxActivations: z.coerce.number().int().min(1, "La cantidad de activaciones debe ser al menos 1."),
  expiresAt: z.string().optional(),
  note: z.string().optional()
});

type IssueFormValues = z.input<typeof issueSchema>;
type IssueValues = z.output<typeof issueSchema>;

const defaults: IssueFormValues = { maxActivations: 1, expiresAt: "", note: "" };

interface IssueKeyDialogProps {
  open: boolean;
  issued: IssuedActivationKey | null;
  onOpenChange: (open: boolean) => void;
  onCreated: (created: IssuedActivationKey) => void;
}

export function IssueKeyDialog({ open, issued, onOpenChange, onCreated }: IssueKeyDialogProps) {
  const [copied, setCopied] = useState(false);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting }
  } = useForm<IssueFormValues, unknown, IssueValues>({
    resolver: zodResolver(issueSchema),
    defaultValues: defaults
  });

  async function onSubmit(values: IssueValues) {
    const payload = {
      maxActivations: values.maxActivations,
      expiresAt: values.expiresAt ? new Date(values.expiresAt).toISOString() : null,
      note: values.note?.trim() ? values.note.trim() : null
    };

    try {
      const created = await callCentral<IssuedActivationKey>("admin/activation/keys", {
        method: "POST",
        body: JSON.stringify(payload)
      });
      reset(defaults);
      setCopied(false);
      onCreated(created);
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo emitir la clave."));
    }
  }

  async function copyKey() {
    if (!issued) {
      return;
    }
    try {
      await navigator.clipboard.writeText(issued.plaintextKey);
      setCopied(true);
      toast.success("Clave copiada.");
    } catch {
      toast.error("No se pudo copiar automáticamente. Copiala a mano.");
    }
  }

  return (
    <Dialog
      open={open}
      disablePointerDismissal={Boolean(issued)}
      onOpenChange={(next, details) => {
        if (
          !next &&
          issued &&
          (details.reason === "escape-key" || details.reason === "outside-press")
        ) {
          return;
        }
        if (!next) {
          reset(defaults);
          setCopied(false);
        }
        onOpenChange(next);
      }}
    >
      <DialogContent showCloseButton={!issued}>
        {issued ? (
          <div className="grid gap-4">
            <DialogHeader>
              <DialogTitle>Clave emitida</DialogTitle>
              <DialogDescription>Esta clave no se vuelve a mostrar. Copiala ahora.</DialogDescription>
            </DialogHeader>
            <Textarea
              readOnly
              value={issued.plaintextKey}
              className="font-mono text-sm"
              onFocus={event => event.currentTarget.select()}
            />
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                Ya la copié, cerrar
              </Button>
              <Button type="button" onClick={() => void copyKey()}>
                {copied ? <Check data-icon="inline-start" /> : <Copy data-icon="inline-start" />}
                {copied ? "Copiada" : "Copiar clave"}
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="grid gap-4">
            <DialogHeader>
              <DialogTitle>Nueva clave</DialogTitle>
            </DialogHeader>

            <FieldGroup className="gap-4">
              <Field data-invalid={errors.maxActivations ? true : undefined}>
                <FieldLabel htmlFor="max-activations">Activaciones máximas</FieldLabel>
                <Input
                  id="max-activations"
                  type="number"
                  min={1}
                  aria-invalid={errors.maxActivations ? true : undefined}
                  {...register("maxActivations")}
                />
                {errors.maxActivations?.message && (
                  <FieldError>{errors.maxActivations.message}</FieldError>
                )}
              </Field>

              <Field>
                <FieldLabel htmlFor="expires-at">Vencimiento (opcional)</FieldLabel>
                <Input id="expires-at" type="date" {...register("expiresAt")} />
              </Field>

              <Field>
                <FieldLabel htmlFor="issue-note">Nota (opcional)</FieldLabel>
                <Textarea
                  id="issue-note"
                  placeholder="Ej. Clave para la instalación de la Sede Norte."
                  {...register("note")}
                />
              </Field>
            </FieldGroup>

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                Cancelar
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Emitiendo…" : "Emitir clave"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
