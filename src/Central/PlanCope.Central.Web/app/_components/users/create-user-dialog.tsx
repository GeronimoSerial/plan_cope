"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Plus } from "lucide-react";
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
import { MIN_PASSWORD_LENGTH, buildCreateUserPayload, canOfferUserCreate } from "./user-mapping";
import type { UserProfile } from "../../_lib/contracts";
import type { UserSummary } from "../../_lib/api/server";

const createUserSchema = z.object({
  email: z.string().trim().min(1, "El correo es requerido.").email("El correo no es válido."),
  password: z
    .string()
    .min(MIN_PASSWORD_LENGTH, `La contraseña debe tener al menos ${MIN_PASSWORD_LENGTH} caracteres.`),
  fullName: z.string().trim().min(1, "El nombre completo es requerido.")
});

type CreateUserValues = z.input<typeof createUserSchema>;
type CreateUserOutput = z.output<typeof createUserSchema>;

const defaults: CreateUserValues = { email: "", password: "", fullName: "" };

interface CreateUserButtonProps {
  user: Pick<UserProfile, "role" | "rosterScope">;
}

export function CreateUserButton({ user }: CreateUserButtonProps) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting }
  } = useForm<CreateUserValues, unknown, CreateUserOutput>({
    resolver: zodResolver(createUserSchema),
    defaultValues: defaults
  });

  const canCreate = canOfferUserCreate(user);

  async function onSubmit(values: CreateUserOutput) {
    try {
      await callCentral<UserSummary>("admin/users", {
        method: "POST",
        body: JSON.stringify(buildCreateUserPayload(values))
      });
      toast.success("Usuario creado.");
      setOpen(false);
      reset(defaults);
      router.refresh();
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo crear el usuario."));
    }
  }

  if (!canCreate) {
    return null;
  }

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <Plus data-icon="inline-start" />
        Nuevo usuario
      </Button>
      <Dialog
        open={open}
        onOpenChange={next => {
          setOpen(next);
          if (!next) {
            reset(defaults);
          }
        }}
      >
        <DialogContent>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="grid gap-4">
            <DialogHeader>
              <DialogTitle>Nuevo usuario</DialogTitle>
              <DialogDescription>La contraseña debe tener al menos {MIN_PASSWORD_LENGTH} caracteres.</DialogDescription>
            </DialogHeader>

            <FieldGroup className="gap-4">
              <Field data-invalid={errors.fullName ? true : undefined}>
                <FieldLabel htmlFor="new-user-full-name">Nombre completo</FieldLabel>
                <Input
                  id="new-user-full-name"
                  autoComplete="name"
                  aria-invalid={errors.fullName ? true : undefined}
                  {...register("fullName")}
                />
                {errors.fullName?.message && <FieldError>{errors.fullName.message}</FieldError>}
              </Field>

              <Field data-invalid={errors.email ? true : undefined}>
                <FieldLabel htmlFor="new-user-email">Correo</FieldLabel>
                <Input
                  id="new-user-email"
                  type="email"
                  autoComplete="email"
                  aria-invalid={errors.email ? true : undefined}
                  {...register("email")}
                />
                {errors.email?.message && <FieldError>{errors.email.message}</FieldError>}
              </Field>

              <Field data-invalid={errors.password ? true : undefined}>
                <FieldLabel htmlFor="new-user-password">Contraseña</FieldLabel>
                <Input
                  id="new-user-password"
                  type="password"
                  autoComplete="new-password"
                  aria-invalid={errors.password ? true : undefined}
                  {...register("password")}
                />
                {errors.password?.message && <FieldError>{errors.password.message}</FieldError>}
              </Field>
            </FieldGroup>

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setOpen(false)}>
                Cancelar
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Creando…" : "Crear usuario"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
