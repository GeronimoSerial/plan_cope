import { useState } from "react";
import { AppShell } from "./components/AppShell";
import { SchoolGate } from "./components/SchoolGate";
import { SessionsWorkspace } from "./components/SessionsWorkspace";
import { WorkspaceModeTabs } from "./components/WorkspaceModeTabs";
import { ExamBuilderPage } from "./exam-builder/ExamBuilderPage";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";

export function HostApp() {
  const hostContext = useHostContext();
  const delivery = useDeliverySession(hostContext);
  const [isSchoolConfirmed, setIsSchoolConfirmed] = useState(false);
  const [workspaceMode, setWorkspaceMode] = useState<"sessions" | "builder">("sessions");

  if (!isSchoolConfirmed) {
    return (
      <SchoolGate
        cue={delivery.sessionForm.form.cue}
        schoolName={delivery.sessionForm.schoolName}
        onCueChange={value => delivery.sessionForm.updateForm("cue", value)}
        onContinue={() => setIsSchoolConfirmed(true)}
      />
    );
  }

  return (
    <AppShell status={delivery.status}>
      <WorkspaceModeTabs mode={workspaceMode} onChange={setWorkspaceMode} />

      {workspaceMode === "sessions" ? (
        <SessionsWorkspace delivery={delivery} />
      ) : (
        <ExamBuilderPage apiBaseUrl={hostContext.apiBaseUrl} />
      )}
    </AppShell>
  );
}
