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
};

type NumberInputProps = {
  value: number;
  min: number;
  max: number;
  onChange: (value: number) => void;
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
  variant?: "primary" | "secondary";
  onClick: () => void;
};

export function Field({ label, error, children }: FieldProps) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {error && <span className="field-error">{error}</span>}
    </label>
  );
}

export function TextInput({ value, onChange, readOnly, placeholder }: TextInputProps) {
  return (
    <input
      className="control"
      value={value}
      readOnly={readOnly}
      placeholder={placeholder}
      onChange={event => onChange(event.target.value)}
    />
  );
}

export function NumberInput({ value, min, max, onChange }: NumberInputProps) {
  return (
    <input
      className="control"
      type="number"
      min={min}
      max={max}
      value={value}
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

export function SectionTitle({ title, description }: { title: string; description: string }) {
  return (
    <div className="section-title">
      <h2>{title}</h2>
      <p>{description}</p>
    </div>
  );
}
