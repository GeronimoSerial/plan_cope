"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { createVersionConfirmation, draftExistsConflict, versionBuilderHref } from "../../_lib/exams/version-state";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import type { ExamVersion } from "../../_lib/contracts";

interface CreateVersionDialogProps {
  examId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sourceVersionId: string;
  sourceNumber: number;
  /** true cuando la fuente es la version publicada actual. */
  sourcePublished: boolean;
  nextNumber: number;
}

// Dialogo de creacion de version copiada. Nunca crea una version vacia: el API deep-copia la
// version fuente. Si el examen ya tiene un borrador, el API responde 409 y abrimos ese borrador.
export function CreateVersionDialog({
  examId,
  open,
  onOpenChange,
  sourceVersionId,
  sourceNumber,
  sourcePublished,
  nextNumber
}: CreateVersionDialogProps) {
  const router = useRouter();
  const [pending, setPending] = useState(false);

  async function createVersion() {
    setPending(true);
    try {
      const created = await callCentral<ExamVersion>(`exams/${encodeURIComponent(examId)}/versions`, {
        method: "POST",
        body: JSON.stringify({ sourceVersionId })
      });
      toast.success(`Versión ${created.versionNumber} creada.`);
      onOpenChange(false);
      router.push(versionBuilderHref(examId, created.id));
    } catch (error) {
      const conflict = draftExistsConflict(error);
      if (conflict) {
        onOpenChange(false);
        toast.info("Ya existe un borrador. Te llevamos a él.");
        router.push(versionBuilderHref(examId, conflict.draftVersionId));
        return;
      }
      toast.error(getErrorMessage(error, "No se pudo crear la versión."));
      setPending(false);
    }
  }

  return (
    <AlertDialog
      open={open}
      onOpenChange={next => {
        onOpenChange(next);
        if (!next) {
          setPending(false);
        }
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Crear versión</AlertDialogTitle>
          <AlertDialogDescription>
            {createVersionConfirmation({ nextNumber, sourceNumber, sourcePublished })}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
          <Button type="button" disabled={pending} onClick={() => void createVersion()}>
            {pending ? "Creando…" : "Crear versión y editar"}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
