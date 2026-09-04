import { ActionButton, Field, TextInput } from "../../shared/ui";
import { CUE_LENGTH, isValidCue, normalizeCueInput } from "../domain/cue";

type SchoolGateProps = {
  cue: string;
  schoolName: string;
  onCueChange: (value: string) => void;
  onContinue: () => void;
};

export function SchoolGate({ cue, schoolName, onCueChange, onContinue }: SchoolGateProps) {
  const cueIsValid = isValidCue(cue);

  return (
    <section className="school-gate">
      <div className="gate-card">
        <p className="eyebrow">MINISTERIO DE EDUCACION | OPERATIVO LOCAL</p>
        <h1>Plan Cope Local</h1>
        <p>
          Identifica la escuela con su CUE para preparar la consola de toma. Luego podras seleccionar curso,
          division y examen para compartir el acceso con los alumnos de la red local.
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

        <Field label="Escuela">
          <TextInput value={schoolName} readOnly onChange={() => undefined} />
        </Field>

        <ActionButton disabled={!cueIsValid} onClick={onContinue}>
          Continuar a la consola
        </ActionButton>
      </div>
    </section>
  );
}
