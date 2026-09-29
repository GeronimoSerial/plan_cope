"use client";

import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
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
import { MIN_PASSWORD_LENGTH } from "./user-mapping";
import type { UserSummary } from "../../_lib/api/server";

const resetPasswordSchema = z.object({
  newPassword: z
    .string()
    .min(MIN_PASSWORD_LENGTH, `La contraseña debe tener al menos ${MIN_PASSWORD_LENGTH} caracteres.`)
});

type ResetPasswordValues = z.infer<typeof resetPasswordSchema>;

interface ResetPasswordDialogProps {
  target: UserSummary;
  onClose: () => void;
}

export function ResetPasswordDialog({ target, onClose }: ResetPasswordDialogProps) {
  const router = useRouter();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<ResetPasswordValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { newPassword: "" }
  });

  async function onSubmit(values: ResetPasswordValues) {
    try {
      await callCentral<undefined>(
        `admin/users/${encodeURIComponent(target.id)}/reset-password`,
        { method: "POST", body: JSON.stringify(values) }
      );
      toast.success("Contraseña restablecida.");
      onClose();
      router.refresh();
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo restablecer la contraseña."));
    }
  }

  return (
    <Dialog
      open
      onOpenChange={next => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent>
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="grid gap-4">
          <DialogHeader>
            <DialogTitle>Restablecer contraseña</DialogTitle>
            <DialogDescription>{target.email}</DialogDescription>
          </DialogHeader>

          <Field data-invalid={errors.newPassword ? true : undefined}>
            <FieldLabel htmlFor="reset-password">Nueva contraseña</FieldLabel>
            <Input
              id="reset-password"
              type="password"
              autoComplete="new-password"
              aria-invalid={errors.newPassword ? true : undefined}
              {...register("newPassword")}
            />
            {errors.newPassword?.message && (
              <FieldError>{errors.newPassword.message}</FieldError>
            )}
          </Field>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Restableciendo…" : "Restablecer contraseña"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
