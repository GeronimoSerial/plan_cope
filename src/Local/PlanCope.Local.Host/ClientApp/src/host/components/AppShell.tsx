import type { ReactNode } from "react";
import { UpdateStatus } from "./UpdateStatus";
import { SyncStatusIndicator } from "./SyncStatusIndicator";
import { useUpdateStatus } from "../hooks/useUpdateStatus";
import { useSyncStatus } from "../hooks/useSyncStatus";

export type HostTab = "home" | "history" | "stats";

type AppShellProps = {
  status: string;
  apiBaseUrl?: string;
  appVersion?: string;
  sessionContext?: { schoolName: string; schoolCode: string } | null;
  activeTab: HostTab;
  onTabChange: (tab: HostTab) => void;
  children: ReactNode;
};

const tabs: Array<{ id: HostTab; label: string }> = [
  { id: "home", label: "Inicio" },
  { id: "history", label: "Historial" },
  { id: "stats", label: "Estadísticas" }
];

export function AppShell({ status, apiBaseUrl, appVersion, sessionContext, activeTab, onTabChange, children }: AppShellProps) {
  const update = useUpdateStatus();
  const sync = useSyncStatus(apiBaseUrl ?? "");

  return (
    <div className="app-page" id="inicio">
      <header className="app-header">
        <div className="institutional-ribbon" aria-hidden="true">
          <span /><span /><span /><span /><span />
        </div>
        <div className="institutional-signature">
          <a className="institutional-logo" href="#inicio" aria-label="Plan COPE, inicio">
            <img src="/static/logo-educacion-h.svg" alt="Gobierno de Corrientes - Ministerio de Educacion" />
          </a>
          <span className="institutional-aside">Provincia de Corrientes<br />República Argentina</span>
        </div>
        <div className="app-nav-band">
          <div className="app-nav-inner">
            <h1 className="app-brand">Plan COPE</h1>
            <nav className="mode-tabs" aria-label="Secciones principales">
              {tabs.map(tab => (
                <button
                  key={tab.id}
                  type="button"
                  className={activeTab === tab.id ? "mode-tab mode-tab-active" : "mode-tab"}
                  aria-current={activeTab === tab.id ? "page" : undefined}
                  onClick={() => onTabChange(tab.id)}
                >
                  {tab.label}
                </button>
              ))}
            </nav>
            <div className="app-header-actions" aria-label="Estado del equipo">
              <div className="app-sync-action"><SyncStatusIndicator status={sync} /></div>
              <div className="app-update-action">
                <UpdateStatus
                  appVersion={appVersion}
                  status={update.status}
                  onCheckForUpdates={update.checkForUpdates}
                  onDownloadUpdate={update.downloadUpdate}
                  onDeferUpdate={update.deferUpdate}
                />
              </div>
            </div>
          </div>
        </div>
      </header>

      <main className="app-content">
        {sessionContext && <div className="session-context" aria-label="Escuela de la sesión"><strong>{sessionContext.schoolName}</strong>{sessionContext.schoolName !== `CUE ${sessionContext.schoolCode}` && <span>CUE {sessionContext.schoolCode}</span>}</div>}
        <div className="workspace">{children}</div>
      </main>
      <footer className="footer">
        <span className="footer-status" role="status" aria-live="polite">{status}</span>
      </footer>
    </div>
  );
}
