"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "../layout/page-header";
import { ActivationKeysTable } from "./activation-keys-table";
import { IssueKeyDialog, type IssuedActivationKey } from "./issue-key-dialog";
import { ReissueKeyDialog } from "./reissue-key-dialog";
import { RevokeKeyDialog } from "./revoke-key-dialog";
import type { ActivationKeySummary } from "../../_lib/api/server";

interface ActivationKeysPanelProps {
  keys: ActivationKeySummary[];
}

export function ActivationKeysPanel({ keys }: ActivationKeysPanelProps) {
  const router = useRouter();
  const [issueOpen, setIssueOpen] = useState(false);
  const [issued, setIssued] = useState<IssuedActivationKey | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ActivationKeySummary | null>(null);
  const [reissueTarget, setReissueTarget] = useState<ActivationKeySummary | null>(null);

  function openIssue() {
    setIssued(null);
    setIssueOpen(true);
  }

  return (
    <>
      <PageHeader
        title="Claves de activación"
        description="Emití una clave a nombre de una persona y administrá desde acá sus equipos activados."
        actions={
          <Button onClick={openIssue}>
            <Plus data-icon="inline-start" />
            Nueva clave
          </Button>
        }
      />

      <ActivationKeysTable
        keys={keys}
        onRevoke={setRevokeTarget}
        onReissue={setReissueTarget}
        onCreate={openIssue}
      />

      <IssueKeyDialog
        open={issueOpen}
        issued={issued}
        onOpenChange={open => {
          setIssueOpen(open);
          if (!open && issued) {
            setIssued(null);
            router.refresh();
          }
        }}
        onCreated={created => {
          setIssued(created);
          router.refresh();
        }}
      />

      {revokeTarget && (
        <RevokeKeyDialog target={revokeTarget} onClose={() => setRevokeTarget(null)} />
      )}

      {reissueTarget && (
        <ReissueKeyDialog
          target={reissueTarget}
          onClose={() => setReissueTarget(null)}
          onIssued={created => {
            setIssued(created);
            setIssueOpen(true);
          }}
        />
      )}
    </>
  );
}
