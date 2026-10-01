import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";

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

export type ComboboxOption = { value: string; label: string; description?: string };
type SearchableComboboxProps = { options: ComboboxOption[]; value: string; onChange: (value: string) => void; placeholder?: string; label: string; id?: string };

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

function normalizeSearch(value: string) {
  return value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("es");
}

export function SearchableCombobox({ options, value, onChange, placeholder, label, id }: SearchableComboboxProps) {
  const generatedId = useId();
  const inputId = id ?? `${generatedId}-input`;
  const listId = `${inputId}-listbox`;
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(-1);
  const selected = options.find(option => option.value === value);
  const normalizedQuery = normalizeSearch(query.trim());
  const filtered = useMemo(() => options.filter(option => normalizeSearch(`${option.label} ${option.description ?? ""}`).includes(normalizedQuery)), [options, normalizedQuery]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => { if (!rootRef.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  const choose = (option: ComboboxOption) => { onChange(option.value); setQuery(""); setOpen(false); setActiveIndex(-1); };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault(); setOpen(true);
      const visibleCount = Math.min(filtered.length, 8);
      setActiveIndex(current => visibleCount ? (current + (event.key === "ArrowDown" ? 1 : -1) + visibleCount) % visibleCount : -1);
    } else if (event.key === "Enter" && open && activeIndex >= 0 && filtered[activeIndex]) {
      event.preventDefault(); choose(filtered[activeIndex]);
    } else if (event.key === "Escape") { setOpen(false); setQuery(""); setActiveIndex(-1); }
    else if (event.key === "Tab") { setOpen(false); setQuery(""); setActiveIndex(-1); }
  };

  return <div className="searchable-combobox" ref={rootRef}>
    <label className="searchable-combobox-label" htmlFor={inputId}>{label}</label>
    <input id={inputId} className="control" role="combobox" aria-autocomplete="list" aria-expanded={open} aria-controls={listId}
      aria-activedescendant={open && activeIndex >= 0 ? `${listId}-option-${activeIndex}` : undefined}
      value={open ? query : selected?.label ?? ""} placeholder={placeholder} onFocus={() => { setQuery(""); setOpen(true); }}
      onChange={event => { setQuery(event.target.value); setActiveIndex(-1); setOpen(true); }} onKeyDown={onKeyDown} />
    {open && <ul id={listId} className="searchable-combobox-list" role="listbox" aria-label={label}>
      {filtered.length ? filtered.slice(0, 8).map((option, index) => <li id={`${listId}-option-${index}`} className={`searchable-combobox-option${index === activeIndex ? " is-active" : ""}`} key={option.value}
        role="option" aria-selected={option.value === value} onMouseDown={event => event.preventDefault()} onClick={() => choose(option)}>
        <strong>{option.label}</strong>{option.description && <span>{option.description}</span>}
      </li>) : <li className="searchable-combobox-empty" role="presentation">Sin resultados</li>}
    </ul>}
  </div>;
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
  const generatedId = useId();
  const titleId = labelledBy ?? `${generatedId}-title`;
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
