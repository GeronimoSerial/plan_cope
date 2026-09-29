"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { loginSchema, type LoginValues } from "../../_lib/schema/auth";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { safeInternalPath } from "../../_lib/safe-redirect";

interface LoginFormProps {
  redirectTo: string;
  expired: boolean;
}

export function LoginForm({ redirectTo, expired }: LoginFormProps) {
  const router = useRouter();
  const [mounted, setMounted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: "", password: "" }
  });

  useEffect(() => {
    setMounted(true);
  }, []);

  async function onSubmit(values: LoginValues) {
    setServerError(null);
    const res = await fetch("/api/session/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values)
    });

    if (!res.ok) {
      const data = (await res.json().catch(() => null)) as { error?: string } | null;
      setServerError(data?.error ?? "No se pudo iniciar sesión.");
      return;
    }

    router.replace(safeInternalPath(redirectTo, window.location.origin));
    router.refresh();
  }

  return (
    <form method="post" onSubmit={handleSubmit(onSubmit)} noValidate className="grid gap-4">
      {expired && !serverError && (
        <Alert>
          <AlertDescription>Tu sesión expiró. Volvé a ingresar.</AlertDescription>
        </Alert>
      )}
      {serverError && (
        <Alert variant="destructive">
          <AlertDescription>{serverError}</AlertDescription>
        </Alert>
      )}

      <FieldGroup className="gap-4">
        <Field data-invalid={errors.username ? true : undefined}>
          <FieldLabel htmlFor="username">Usuario</FieldLabel>
          <Input
            id="username"
            autoComplete="username"
            aria-invalid={errors.username ? true : undefined}
            {...register("username")}
          />
          {errors.username?.message && <FieldError>{errors.username.message}</FieldError>}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">Contraseña</FieldLabel>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            aria-invalid={errors.password ? true : undefined}
            {...register("password")}
          />
          {errors.password?.message && <FieldError>{errors.password.message}</FieldError>}
        </Field>
      </FieldGroup>

      <Button type="submit" disabled={!mounted || isSubmitting} className="w-full">
        {isSubmitting ? "Ingresando…" : "Ingresar"}
      </Button>
    </form>
  );
}
