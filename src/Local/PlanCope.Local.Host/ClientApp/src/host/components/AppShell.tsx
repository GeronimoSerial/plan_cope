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
  activeSessionGradeLabel?: string | null;
  activeTab: HostTab;
  onTabChange: (tab: HostTab) => void;
  children: ReactNode;
};

const tabs: Array<{ id: HostTab; label: string }> = [
  { id: "home", label: "Inicio" },
  { id: "history", label: "Historial" },
  { id: "stats", label: "Estadísticas" }
];

export function AppShell({ status, apiBaseUrl, appVersion, activeSessionGradeLabel, activeTab, onTabChange, children }: AppShellProps) {
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
            <a className="app-brand" href="#inicio" aria-label="Plan COPE, inicio">Plan COPE</a>
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

      {activeSessionGradeLabel && <p className="topbar-session-grade">Sesión activa · {activeSessionGradeLabel}</p>}
      <main className="app-content">
        <div className="workspace">{children}</div>
        <footer className="footer">
          <span className="footer-status" role="status" aria-live="polite">{status}</span>
        </footer>
      </main>
    </div>
  );
}
