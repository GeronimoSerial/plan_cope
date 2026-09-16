import { redirect } from "next/navigation";
import type { Metadata } from "next";
import {
  isSessionExpired,
  listUnassignedGradingPolicies,
  type UnassignedExamVersion
} from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { LegacyPolicyPanel } from "../../_components/legacy-policy/legacy-policy-panel";

export const metadata: Metadata = { title: "Políticas de puntaje heredadas · PlanCope Central" };

export default async function LegacyGradingPoliciesPage() {
  let versions: UnassignedExamVersion[];
  try {
    versions = await listUnassignedGradingPolicies();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Políticas de puntaje"
        title="Políticas de puntaje heredadas"
        description="Asigná una regla de puntaje a versiones publicadas que no la tienen."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Políticas de puntaje heredadas" }]}
      />

      <LegacyPolicyPanel initialVersions={versions} />
    </>
  );
}
