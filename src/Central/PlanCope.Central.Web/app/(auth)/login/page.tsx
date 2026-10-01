import type { Metadata } from "next";
import Image from "next/image";
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
    <main className="central-login">
      <div className="central-ribbon" aria-hidden="true"><span /><span /><span /><span /><span /></div>
      <div className="central-signature">
        <div className="central-signature__inner">
          <Image className="central-logo" src="/marca/logo-educacion-h.svg" width={300} height={60} priority alt="Gobierno de Corrientes - Ministerio de Educación" />
          <span className="central-reparticiones">Dirección de Planeamiento e Investigación Educativa</span>
          <span className="central-province">Provincia de Corrientes<br />República Argentina</span>
        </div>
      </div>
      <div className="central-login__content">
      <div className="central-login__card">
        <div className="mb-4 font-heading text-xl font-extrabold uppercase">Plan COPE · Central</div>
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
      </div>
      <footer className="central-footer"><div className="central-footer__inner">Ministerio de Educación · Gobierno de Corrientes</div></footer>
    </main>
  );
}
