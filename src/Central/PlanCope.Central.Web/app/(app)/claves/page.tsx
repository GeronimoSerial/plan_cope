import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listActivationKeys, type ActivationKeySummary } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { ActivationKeysPanel } from "../../_components/keys/activation-keys-panel";

export const metadata: Metadata = { title: "Claves de activación · PlanCope Central" };

export default async function ActivationKeysPage() {
  let keys: ActivationKeySummary[];
  try {
    keys = await listActivationKeys();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Claves de activación"
        title="Claves de activación"
        description="Administrá las claves de activación de la instalación."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Claves de activación" }]}
      />

      <ActivationKeysPanel initialKeys={keys} />
    </>
  );
}
