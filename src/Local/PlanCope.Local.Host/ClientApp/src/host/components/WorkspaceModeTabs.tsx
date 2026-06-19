type WorkspaceMode = "sessions" | "builder";

type WorkspaceModeTabsProps = {
  mode: WorkspaceMode;
  onChange: (mode: WorkspaceMode) => void;
};

export function WorkspaceModeTabs({ mode, onChange }: WorkspaceModeTabsProps) {
  return (
    <div className="mode-tabs">
      <button
        className={mode === "sessions" ? "mode-tab mode-tab-active" : "mode-tab"}
        type="button"
        onClick={() => onChange("sessions")}
      >
        Toma local
      </button>
      <button
        className={mode === "builder" ? "mode-tab mode-tab-active" : "mode-tab"}
        type="button"
        onClick={() => onChange("builder")}
      >
        Creador de examenes
      </button>
    </div>
  );
}
