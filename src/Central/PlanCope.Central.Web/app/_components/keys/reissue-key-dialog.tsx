"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import { Field, FieldLabel } from "@/components/ui/field";
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
import { buildReissuePayload } from "./activation-key-mapping";
import type { IssuedActivationKey } from "./issue-key-dialog";
import type { ActivationKeySummary } from "../../_lib/api/server";

interface ReissueKeyDialogProps {
  target: ActivationKeySummary;
  onClose: () => void;
  onIssued: (created: IssuedActivationKey) => void;
}

export function ReissueKeyDialog({ target, onClose, onIssued }: ReissueKeyDialogProps) {
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [holderName, setHolderName] = useState(target.holderName ?? "");

  async function confirm() {
    setPending(true);

    try {
      await callCentral<undefined>(`admin/activation/keys/${encodeURIComponent(target.id)}/revoke`, {
        method: "POST",
        body: JSON.stringify({ reason: "Reemitida" })
      });
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo revocar la clave para reemitirla."));
      setPending(false);
      return;
    }

    try {
      const created = await callCentral<IssuedActivationKey>("admin/activation/keys", {
        method: "POST",
        body: JSON.stringify(buildReissuePayload({ ...target, holderName }))
      });
      onIssued(created);
      onClose();
      router.refresh();
    } catch (error) {
      toast.error(
        "La clave anterior ya fue revocada, pero no se pudo emitir la nueva: " +
          getErrorMessage(error, "error desconocido") +
          ". Emití una clave nueva manualmente."
      );
      setPending(false);
      router.refresh();
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
          <AlertDialogTitle>Reemitir clave {target.keyPrefix}</AlertDialogTitle>
          <AlertDialogDescription>
            Se revoca la clave actual y se emite una nueva para la misma persona, con las mismas
            activaciones máximas, vencimiento y nota. La clave nueva se muestra una sola vez.
          </AlertDialogDescription>
        </AlertDialogHeader>
        {!target.holderName?.trim() && (
          <Field>
            <FieldLabel htmlFor="reissue-holder">A nombre de</FieldLabel>
            <Input id="reissue-holder" maxLength={200} value={holderName} onChange={event => setHolderName(event.target.value)} />
          </Field>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
          <AlertDialogAction disabled={pending || !holderName.trim()} onClick={() => void confirm()}>
            {pending ? "Reemitiendo…" : "Reemitir clave"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
