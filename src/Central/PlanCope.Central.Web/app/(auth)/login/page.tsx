import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getSessionUser } from "../../_lib/server/session";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { LoginForm } from "./login-form";

export const metadata: Metadata = {
  title: "Ingresar · PlanCope Central"
};

function safeRedirect(from: string | undefined): string {
  // Solo permitimos rutas internas (evita open-redirect).
  if (from && from.startsWith("/") && !from.startsWith("//")) {
    return from;
  }
  return "/dashboard";
}

export default async function LoginPage({
  searchParams
}: {
  searchParams: Promise<{ from?: string; expired?: string }>;
}) {
  const user = await getSessionUser();
  if (user) {
    redirect("/dashboard");
  }

  const params = await searchParams;
  const redirectTo = safeRedirect(params.from);
  const expired = params.expired === "1";

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
