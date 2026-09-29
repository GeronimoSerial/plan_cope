import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listRegisteredNodes, type RegisteredNodeSummary } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { NodeRegistryPanel } from "../../_components/nodes/node-registry-panel";

export const metadata: Metadata = { title: "Nodos registrados · PlanCope Central" };

export default async function RegisteredNodesPage() {
  let nodes: RegisteredNodeSummary[];
  try {
    nodes = await listRegisteredNodes();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader title="Nodos registrados" />
      <NodeRegistryPanel initialNodes={nodes} />
    </>
  );
}
