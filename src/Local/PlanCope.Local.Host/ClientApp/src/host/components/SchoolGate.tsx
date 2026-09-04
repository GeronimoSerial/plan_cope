import { ActionButton, Field, TextInput } from "../../shared/ui";
import { CUE_LENGTH, isValidCue, normalizeCueInput } from "../domain/cue";

type SchoolGateProps = {
  cue: string;
  schoolName: string;
  hasRoster: boolean;
  isLoadingRoster: boolean;
  rosterError: string | null;
  onCueChange: (value: string) => void;
  onContinue: () => void;
};

export function SchoolGate({
  cue,
  schoolName,
  hasRoster,
  isLoadingRoster,
  rosterError,
  onCueChange,
  onContinue
}: SchoolGateProps) {
  const cueIsValid = isValidCue(cue);

  return (
    <section className="school-gate">
      <div className="gate-card">
        <p className="eyebrow">MINISTERIO DE EDUCACION | OPERATIVO LOCAL</p>
        <h1>Plan Cope Local</h1>
        <p>
          Ingresá el CUE de la escuela. Usaremos el padrón de Gestión Educativa guardado en este equipo.
        </p>

        <Field label="CUE" error={cue.length > 0 && !cueIsValid ? `El CUE debe tener ${CUE_LENGTH} dígitos (incluye el anexo).` : undefined}>
          <TextInput
            value={cue}
            placeholder="Ej. 180055400"
            inputMode="numeric"
            maxLength={CUE_LENGTH}
            autoComplete="off"
            onChange={value => onCueChange(normalizeCueInput(value))}
          />
        </Field>

        {cueIsValid && (
          <div className={`school-confirmation${hasRoster ? " school-confirmation-ready" : ""}`} role="status">
            <span>{isLoadingRoster ? "Buscando el padrón…" : hasRoster ? "Escuela encontrada" : "Padrón no disponible"}</span>
            {!isLoadingRoster && hasRoster && <strong>{schoolName || `CUE ${cue}`}</strong>}
            {!isLoadingRoster && !hasRoster && (
              <small>No hay un padrón sincronizado para este CUE en el equipo.</small>
            )}
          </div>
        )}

        {rosterError && <p className="error-banner" role="alert">{rosterError}</p>}

        <ActionButton disabled={!cueIsValid || isLoadingRoster || !hasRoster} onClick={onContinue}>
          Continuar
        </ActionButton>
      </div>
    </section>
  );
}
