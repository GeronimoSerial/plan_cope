import type { ReactNode } from "react";
import { UpdateStatus } from "./UpdateStatus";
import { SyncStatusIndicator } from "./SyncStatusIndicator";
import { useUpdateStatus } from "../hooks/useUpdateStatus";
import { useSyncStatus } from "../hooks/useSyncStatus";

type AppShellProps = {
  status: string;
  apiBaseUrl?: string;
  appVersion?: string;
  activeSessionGradeLabel?: string | null;
  children: ReactNode;
};

export function AppShell({ status, apiBaseUrl, appVersion, activeSessionGradeLabel, children }: AppShellProps) {
  const update = useUpdateStatus();
  const sync = useSyncStatus(apiBaseUrl ?? "");

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand-mark" aria-hidden="true">
          <span />
        </div>
        <div className="topbar-copy">
          <h1>Plan Cope Local</h1>
          <p>Gestión de sesiones escolares</p>
        </div>
        {activeSessionGradeLabel && <p className="topbar-session-grade">Sesión activa · {activeSessionGradeLabel}</p>}
      </header>

      <div className="workspace">{children}</div>

      <footer className="footer">
        <span className="footer-status" role="status" aria-live="polite">{status}</span>
        <UpdateStatus
          appVersion={appVersion}
          status={update.status}
          onCheckForUpdates={update.checkForUpdates}
          onDownloadUpdate={update.downloadUpdate}
          onDeferUpdate={update.deferUpdate}
        />
        <SyncStatusIndicator status={sync} />
      </footer>
    </main>
  );
}
