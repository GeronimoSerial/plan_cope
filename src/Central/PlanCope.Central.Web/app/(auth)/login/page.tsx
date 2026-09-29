import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getAccessToken } from "../../_lib/server/session";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { LoginForm } from "./login-form";
import { safeInternalPath } from "../../_lib/safe-redirect";

export const metadata: Metadata = {
  title: "Ingresar · PlanCope Central"
};

export default async function LoginPage({
  searchParams
}: {
  searchParams: Promise<{ from?: string; expired?: string }>;
}) {
  const params = await searchParams;
  const expired = params.expired === "1";
  const hasAccess = Boolean(await getAccessToken());
  if (hasAccess && !expired) {
    redirect("/dashboard");
  }
  const redirectTo = safeInternalPath(params.from, "https://central.invalid");

  return (
    <main className="grid min-h-svh place-items-center bg-muted/40 p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center justify-center gap-3">
          <span
            className="grid size-10 place-items-center rounded-lg bg-primary font-bold text-primary-foreground"
            aria-hidden="true"
          >
            PC
          </span>
          <div className="leading-tight">
            <strong className="block text-base">PlanCope Central</strong>
            <span className="text-sm text-muted-foreground">Administración de exámenes</span>
          </div>
        </div>
        <Card>
          <CardHeader>
            <h1 className="font-heading text-lg leading-snug font-medium">Iniciar sesión</h1>
            <CardDescription>Ingresá con tu cuenta institucional.</CardDescription>
          </CardHeader>
          <CardContent>
            <LoginForm redirectTo={redirectTo} expired={expired} />
          </CardContent>
        </Card>
      </div>
    </main>
  );
}
