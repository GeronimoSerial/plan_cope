"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import { Field, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import type { ActivationKeySummary } from "../../_lib/api/server";

interface RevokeKeyDialogProps {
  target: ActivationKeySummary;
  onClose: () => void;
}

export function RevokeKeyDialog({ target, onClose }: RevokeKeyDialogProps) {
  const router = useRouter();
  const [reason, setReason] = useState("");
  const [pending, setPending] = useState(false);

  async function confirm() {
    setPending(true);
    try {
      await callCentral<undefined>(`admin/activation/keys/${encodeURIComponent(target.id)}/revoke`, {
        method: "POST",
        body: JSON.stringify({ reason: reason.trim() })
      });
      toast.success("Clave revocada.");
      onClose();
      router.refresh();
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo revocar la clave."));
      setPending(false);
    }
  }

  return (
    <AlertDialog
      open
      onOpenChange={next => {
        if (!next) {
          onClose();
        }
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Revocar clave {target.keyPrefix}</AlertDialogTitle>
          <AlertDialogDescription>
            La clave deja de ser válida de inmediato. Esta acción no se puede deshacer.
          </AlertDialogDescription>
        </AlertDialogHeader>

        <Field>
          <FieldLabel htmlFor="revoke-reason">Motivo (opcional)</FieldLabel>
          <Input
            id="revoke-reason"
            value={reason}
            placeholder="Ej. Clave comprometida"
            onChange={event => setReason(event.target.value)}
          />
        </Field>

        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
          <AlertDialogAction variant="destructive" disabled={pending} onClick={() => void confirm()}>
            {pending ? "Revocando…" : "Revocar clave"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
