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
import type { UserSummary } from "../../_lib/api/server";

interface DeactivateUserDialogProps {
  target: UserSummary;
  onClose: () => void;
}

export function DeactivateUserDialog({ target, onClose }: DeactivateUserDialogProps) {
  const router = useRouter();
  const [pending, setPending] = useState(false);

  async function confirm() {
    setPending(true);
    try {
      await callCentral<undefined>(`admin/users/${encodeURIComponent(target.id)}/deactivate`, {
        method: "POST"
      });
      toast.success("Usuario desactivado.");
      onClose();
      router.refresh();
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo desactivar el usuario."));
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
          <AlertDialogTitle>Desactivar usuario</AlertDialogTitle>
          <AlertDialogDescription>
            {target.email} dejará de poder iniciar sesión. ¿Querés continuar?
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Cancelar</AlertDialogCancel>
          <AlertDialogAction variant="destructive" disabled={pending} onClick={() => void confirm()}>
            {pending ? "Desactivando…" : "Desactivar"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
