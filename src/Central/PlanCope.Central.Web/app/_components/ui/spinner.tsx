interface SpinnerProps {
  label?: string;
}

export function Spinner({ label = "Cargando…" }: SpinnerProps) {
  return (
    <div className="loading-row" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}
