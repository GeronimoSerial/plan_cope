import type { ExamOption } from "../types";
import { ActionButton, Field, NumberInput, SectionTitle, SelectInput, TextInput } from "./ui";

type SessionFormPanelProps = {
  classroomCode: string;
  courses: string[];
  divisions: string[];
  exams: ExamOption[];
  expectedStudentCount: number;
  isBusy: boolean;
  operatorName: string;
  schoolCode: string;
  schoolName: string;
  selectedCourse: string;
  selectedDivision: string;
  selectedExamId: string;
  onClassroomCodeChange: (value: string) => void;
  onCourseChange: (value: string) => void;
  onCreateSession: () => void;
  onDivisionChange: (value: string) => void;
  onExpectedStudentCountChange: (value: number) => void;
  onOperatorNameChange: (value: string) => void;
  onSelectedExamChange: (value: string) => void;
};

export function SessionFormPanel(props: SessionFormPanelProps) {
  const examOptions = props.exams.map(exam => ({ value: exam.id, label: exam.displayName }));

  return (
    <section className="panel">
      <SectionTitle
        title="Datos de la toma"
        description="Selecciona el examen local y define los datos operativos del aula."
      />

      <div className="form-grid">
        <Field label="Curso">
          <SelectInput
            value={props.selectedCourse}
            options={props.courses.map(value => ({ value, label: value }))}
            onChange={props.onCourseChange}
          />
        </Field>

        <Field label="Division">
          <SelectInput
            value={props.selectedDivision}
            options={props.divisions.map(value => ({ value, label: value }))}
            onChange={props.onDivisionChange}
          />
        </Field>
      </div>

      <Field label="Examen">
        <SelectInput
          value={props.selectedExamId}
          options={examOptions}
          emptyLabel="Sin examenes disponibles"
          onChange={props.onSelectedExamChange}
        />
      </Field>

      <div className="form-grid">
        <Field label="CUE">
          <TextInput value={props.schoolCode} readOnly onChange={() => undefined} />
        </Field>

        <Field label="Escuela">
          <TextInput value={props.schoolName} readOnly onChange={() => undefined} />
        </Field>
      </div>

      <div className="form-grid">
        <Field label="Curso y division de la toma">
          <TextInput value={props.classroomCode} onChange={props.onClassroomCodeChange} />
        </Field>

        <Field label="Alumnos esperados">
          <NumberInput
            min={1}
            max={500}
            value={props.expectedStudentCount}
            onChange={props.onExpectedStudentCountChange}
          />
        </Field>
      </div>

      <Field label="Operador">
        <TextInput value={props.operatorName} onChange={props.onOperatorNameChange} />
      </Field>

      <ActionButton disabled={props.isBusy || !props.selectedExamId} onClick={props.onCreateSession}>
        {props.isBusy ? "Creando sesion..." : "Crear sesion de toma"}
      </ActionButton>
    </section>
  );
}
