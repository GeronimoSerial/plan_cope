import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listActivationKeys, type ActivationKeySummary } from "../../_lib/api/server";
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

  return <ActivationKeysPanel keys={keys} />;
}
