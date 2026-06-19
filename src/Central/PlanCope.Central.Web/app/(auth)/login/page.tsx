import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getSessionUser } from "../../_lib/server/session";
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
    <main className="auth-screen">
      <div className="auth-card">
        <div className="auth-card__brand">
          <span className="sidebar__mark" aria-hidden="true">
            PC
          </span>
          <div>
            <strong style={{ fontSize: 18 }}>PlanCope Central</strong>
            <p style={{ color: "var(--text-muted)", fontSize: 13 }}>Administración y builder de exámenes</p>
          </div>
        </div>
        <h1 style={{ fontSize: 22, marginBottom: 4 }}>Iniciar sesión</h1>
        <p style={{ color: "var(--text-muted)", fontSize: 14 }}>Ingresá con tu cuenta institucional.</p>
        <LoginForm redirectTo={redirectTo} expired={expired} />
      </div>
    </main>
  );
}
