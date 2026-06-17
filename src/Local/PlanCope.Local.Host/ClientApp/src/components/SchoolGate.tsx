import { ActionButton, Field, TextInput } from "./ui";

type SchoolGateProps = {
  cue: string;
  schoolName: string;
  onCueChange: (value: string) => void;
  onContinue: () => void;
};

export function SchoolGate({ cue, schoolName, onCueChange, onContinue }: SchoolGateProps) {
  return (
    <section className="school-gate">
      <div className="gate-card">
        <p className="eyebrow">MINISTERIO DE EDUCACION | OPERATIVO LOCAL</p>
        <h1>Plan Cope Local</h1>
        <p>
          Identifica la escuela con su CUE para preparar la consola de toma. Luego podras seleccionar curso,
          division y examen para compartir el acceso con los alumnos de la red local.
        </p>

        <Field label="CUE">
          <TextInput value={cue} placeholder="Ej. 123456789" onChange={value => onCueChange(value.toUpperCase())} />
        </Field>

        <Field label="Escuela">
          <TextInput value={schoolName} readOnly onChange={() => undefined} />
        </Field>

        <ActionButton disabled={!cue.trim()} onClick={onContinue}>
          Continuar a la consola
        </ActionButton>
      </div>
    </section>
  );
}
