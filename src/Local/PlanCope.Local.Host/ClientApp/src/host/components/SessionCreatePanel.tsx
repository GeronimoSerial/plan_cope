import type { ExamOption, FormErrors } from "../types";
import type { SessionForm } from "../domain/sessionForm";
import { ActionButton, Field, NumberInput, SectionTitle, SelectInput, TextInput } from "../../shared/ui";

type SessionCreatePanelProps = {
  courses: string[];
  divisions: string[];
  exams: ExamOption[];
  form: SessionForm;
  formErrors: FormErrors;
  schoolName: string;
  selectedCourse: string;
  selectedDivision: string;
  selectedExamId: string;
  isBusy: boolean;
  isLoadingExams: boolean;
  onClassroomCodeChange: (value: string) => void;
  onCourseChange: (value: string) => void;
  onCreateSession: () => void;
  onDivisionChange: (value: string) => void;
  onExpectedStudentCountChange: (value: number) => void;
  onOperatorNameChange: (value: string) => void;
  onRefreshExams: () => void;
  onSelectedExamChange: (value: string) => void;
};

export function SessionCreatePanel({
  courses,
  divisions,
  exams,
  form,
  formErrors,
  schoolName,
  selectedCourse,
  selectedDivision,
  selectedExamId,
  isBusy,
  isLoadingExams,
  onClassroomCodeChange,
  onCourseChange,
  onCreateSession,
  onDivisionChange,
  onExpectedStudentCountChange,
  onOperatorNameChange,
  onRefreshExams,
  onSelectedExamChange
}: SessionCreatePanelProps) {
  const examOptions = exams.map(exam => ({ value: exam.id, label: exam.displayName }));

  return (
    <section className="panel">
      <SectionTitle
        title="Datos de la toma"
        description="Selecciona el examen local y define los datos operativos del aula."
      />

      <div className="form-grid">
        <Field label="Curso">
          <SelectInput
            value={selectedCourse}
            options={courses.map(value => ({ value, label: value }))}
            onChange={onCourseChange}
          />
        </Field>

        <Field label="Division">
          <SelectInput
            value={selectedDivision}
            options={divisions.map(value => ({ value, label: value }))}
            onChange={onDivisionChange}
          />
        </Field>
      </div>

      <Field label="Examen" error={formErrors.selectedExamId}>
        <SelectInput
          value={selectedExamId}
          options={examOptions}
          emptyLabel={isLoadingExams ? "Cargando examenes..." : "Sin examenes disponibles"}
          onChange={onSelectedExamChange}
        />
      </Field>

      <div className="form-grid">
        <Field label="CUE" error={formErrors.cue}>
          <TextInput value={form.cue} readOnly onChange={() => undefined} />
        </Field>

        <Field label="Escuela">
          <TextInput value={schoolName} readOnly onChange={() => undefined} />
        </Field>
      </div>

      <div className="form-grid">
        <Field label="Curso y division de la toma" error={formErrors.classroomCode}>
          <TextInput value={form.classroomCode} onChange={onClassroomCodeChange} />
        </Field>

        <Field label="Alumnos esperados" error={formErrors.expectedStudentCount}>
          <NumberInput
            min={1}
            max={500}
            value={form.expectedStudentCount}
            onChange={onExpectedStudentCountChange}
          />
        </Field>
      </div>

      <Field label="Operador" error={formErrors.operatorName}>
        <TextInput value={form.operatorName} onChange={onOperatorNameChange} />
      </Field>

      {exams.length === 0 && !isLoadingExams && (
        <div className="empty-state">
          <strong>No hay examenes locales.</strong>
          <span>Sincroniza o publica un examen en este equipo antes de crear una toma.</span>
          <ActionButton variant="secondary" disabled={isBusy} onClick={onRefreshExams}>
            Reintentar carga
          </ActionButton>
        </div>
      )}

      <ActionButton disabled={isBusy || isLoadingExams || !selectedExamId} onClick={onCreateSession}>
        {isBusy ? "Creando sesion..." : "Crear sesion de toma"}
      </ActionButton>
    </section>
  );
}
