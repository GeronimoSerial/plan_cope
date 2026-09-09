type UpdateStatusProps = {
  appVersion: string;
  onCheckForUpdates: () => void;
  isChecking?: boolean;
  statusMessage?: string;
};

export function UpdateStatus({ appVersion, onCheckForUpdates, isChecking = false, statusMessage }: UpdateStatusProps) {
  return (
    <div className="update-status">
      <p>Version {appVersion}</p>
      <button type="button" onClick={onCheckForUpdates} disabled={isChecking}>
        {isChecking ? "Checking…" : "Check for updates"}
      </button>
      {statusMessage && <p role="status">{statusMessage}</p>}
    </div>
  );
}