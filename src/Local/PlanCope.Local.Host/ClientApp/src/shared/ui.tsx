import type { ReactNode } from "react";

type FieldProps = {
  label: string;
  error?: string;
  children: ReactNode;
};

type TextInputProps = {
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  placeholder?: string;
  inputMode?: "text" | "numeric";
  maxLength?: number;
  autoComplete?: string;
};

type NumberInputProps = {
  value: number;
  min: number;
  max: number;
  onChange: (value: number) => void;
  readOnly?: boolean;
};

type SelectOption = {
  value: string;
  label: string;
};

type SelectInputProps = {
  value: string;
  options: SelectOption[];
  emptyLabel?: string;
  onChange: (value: string) => void;
};

type ButtonProps = {
  children: ReactNode;
  disabled?: boolean;
  variant?: "primary" | "secondary" | "danger";
  onClick: () => void;
};

type Tone = "neutral" | "success" | "warning" | "danger" | "info";

export function Field({ label, error, children }: FieldProps) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {error && <span className="field-error">{error}</span>}
    </label>
  );
}

export function TextInput({ value, onChange, readOnly, placeholder, inputMode, maxLength, autoComplete }: TextInputProps) {
  return (
    <input
      className="control"
      value={value}
      readOnly={readOnly}
      placeholder={placeholder}
      inputMode={inputMode}
      maxLength={maxLength}
      autoComplete={autoComplete}
      onChange={event => onChange(event.target.value)}
    />
  );
}

export function NumberInput({ value, min, max, onChange, readOnly }: NumberInputProps) {
  return (
    <input
      className="control"
      type="number"
      min={min}
      max={max}
      value={value}
      readOnly={readOnly}
      onChange={event => onChange(Number(event.target.value))}
    />
  );
}

export function SelectInput({ value, options, emptyLabel = "Todos", onChange }: SelectInputProps) {
  return (
    <select className="control" value={value} onChange={event => onChange(event.target.value)}>
      <option value="">{emptyLabel}</option>
      {options.map(option => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

export function ActionButton({ children, disabled, variant = "primary", onClick }: ButtonProps) {
  return (
    <button className={`button button-${variant}`} disabled={disabled} type="button" onClick={onClick}>
      {children}
    </button>
  );
}

export function SectionTitle({ title, description }: { title: string; description?: string }) {
  return (
    <div className="section-title">
      <h2>{title}</h2>
      {description && <p>{description}</p>}
    </div>
  );
}

export function Badge({ children, tone = "neutral", large = false }: { children: ReactNode; tone?: Tone; large?: boolean }) {
  return <span className={`badge badge-${tone}${large ? " badge-lg" : ""}`}>{children}</span>;
}

export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`card${className ? ` ${className}` : ""}`}>{children}</section>;
}

export function DataTable({ children, label }: { children: ReactNode; label?: string }) {
  return <div className="data-table-wrap" aria-label={label}>{children}</div>;
}

export function Dialog({ children, title, actions, labelledBy }: { children: ReactNode; title: string; actions?: ReactNode; labelledBy?: string }) {
  const titleId = labelledBy ?? "shared-dialog-title";
  return (
    <div className="dialog-backdrop">
      <section className="dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 className="dialog-title" id={titleId}>{title}</h2>
        <div>{children}</div>
        {actions && <div className="dialog-actions">{actions}</div>}
      </section>
    </div>
  );
}

export function MessageBar({ children, title, tone = "info" }: { children: ReactNode; title?: string; tone?: Exclude<Tone, "neutral"> }) {
  return (
    <div className={`messagebar messagebar-${tone}`} role={tone === "danger" ? "alert" : "status"}>
      {title && <strong className="messagebar-title">{title}</strong>}
      {children}
    </div>
  );
}
