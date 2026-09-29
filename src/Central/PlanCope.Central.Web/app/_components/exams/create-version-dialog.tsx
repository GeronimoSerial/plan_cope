"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { createVersionConfirmation, draftExistsMessage } from "../../_lib/exams/version-state";
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

export interface DraftRef {
  id: string;
  versionNumber: number;
}

interface CreateVersionDialogProps {
  examId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sourceVersionId: string;
  sourceNumber: number;
  /** true cuando la fuente es la version publicada actual. */
  sourcePublished: boolean;
  nextNumber: number;
  /** Borrador existente del examen, si lo hay. */
  draft?: DraftRef | null;
}

// Dialogo de creacion de version copiada. Nunca crea una version vacia: el API deep-copia la
// version fuente. Si ya hay un borrador, ofrece abrirlo antes que crear otro.
export function CreateVersionDialog({
  examId,
  open,
  onOpenChange,
  sourceVersionId,
  sourceNumber,
  sourcePublished,
  nextNumber,
  draft = null
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
      router.push(`/exams/${examId}/versions/${created.id}/builder`);
    } catch (error) {
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
            {draft
              ? draftExistsMessage(draft.versionNumber)
              : createVersionConfirmation({ nextNumber, sourceNumber, sourcePublished })}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          {draft ? (
            <>
              <Button
                type="button"
                variant="outline"
                disabled={pending}
                render={<Link href={`/exams/${examId}/versions/${draft.id}/builder`} />}
                onClick={() => onOpenChange(false)}
              >
                Abrir el borrador
              </Button>
              <Button type="button" disabled={pending} onClick={() => void createVersion()}>
                {pending ? "Creando…" : "Crear igualmente"}
              </Button>
            </>
          ) : (
            <>
              <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
              <Button type="button" disabled={pending} onClick={() => void createVersion()}>
                {pending ? "Creando…" : "Crear versión y editar"}
              </Button>
            </>
          )}
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
