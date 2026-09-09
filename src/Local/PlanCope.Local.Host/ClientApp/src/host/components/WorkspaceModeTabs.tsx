type WorkspaceMode = "sessions" | "builder";

type WorkspaceModeTabsProps = {
  mode: WorkspaceMode;
  onChange: (mode: WorkspaceMode) => void;
};

export function WorkspaceModeTabs({ mode, onChange }: WorkspaceModeTabsProps) {
  return (
    <nav className="mode-tabs" aria-label="Sección de trabajo">
      <button
        className={mode === "sessions" ? "mode-tab mode-tab-active" : "mode-tab"}
        type="button"
        aria-pressed={mode === "sessions"}
        onClick={() => onChange("sessions")}
      >
        Sesiones
      </button>
      <button
        className={mode === "builder" ? "mode-tab mode-tab-active mode-tab-secondary" : "mode-tab mode-tab-secondary"}
        type="button"
        aria-pressed={mode === "builder"}
        onClick={() => onChange("builder")}
      >
        Creador de exámenes
      </button>
    </nav>
  );
}
