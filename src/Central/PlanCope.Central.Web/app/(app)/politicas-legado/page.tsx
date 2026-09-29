import { redirect } from "next/navigation";
import type { Metadata } from "next";
import {
  isSessionExpired,
  listUnassignedGradingPolicies,
  type UnassignedExamVersion
} from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { LegacyPolicyPanel } from "../../_components/legacy-policy/legacy-policy-panel";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";

export const metadata: Metadata = { title: "Reglas de puntaje pendientes · PlanCope Central" };

export default async function LegacyGradingPoliciesPage() {
  let versions: UnassignedExamVersion[];
  try {
    versions = await listUnassignedGradingPolicies();
  } catch (error) {
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired("/politicas-legado");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        title="Reglas de puntaje pendientes"
        description="Versiones publicadas que todavía no tienen regla de puntaje."
      />
      <LegacyPolicyPanel initialVersions={versions} />
    </>
  );
}
