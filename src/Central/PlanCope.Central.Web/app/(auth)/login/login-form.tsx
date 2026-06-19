"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { loginSchema, type LoginValues } from "../../_lib/schema/auth";
import { TextField } from "../../_components/ui/text-field";
import { Button } from "../../_components/ui/button";
import { Banner } from "../../_components/ui/banner";

interface LoginFormProps {
  redirectTo: string;
  expired: boolean;
}

export function LoginForm({ redirectTo, expired }: LoginFormProps) {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: "", password: "" }
  });

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

    router.replace(redirectTo);
    router.refresh();
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      {expired && !serverError && <Banner tone="info">Tu sesión expiró. Volvé a ingresar.</Banner>}
      {serverError && <Banner tone="error">{serverError}</Banner>}

      <div style={{ marginTop: "var(--space-4)" }}>
        <TextField
          label="Usuario"
          autoComplete="username"
          required
          error={errors.username?.message}
          {...register("username")}
        />
        <TextField
          label="Contraseña"
          type="password"
          autoComplete="current-password"
          required
          error={errors.password?.message}
          {...register("password")}
        />
        <Button type="submit" block disabled={isSubmitting}>
          {isSubmitting ? "Ingresando…" : "Ingresar"}
        </Button>
      </div>
    </form>
  );
}
