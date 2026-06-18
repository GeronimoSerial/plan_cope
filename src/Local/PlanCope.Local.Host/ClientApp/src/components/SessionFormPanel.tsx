import type { ExamOption, FormErrors, LocalSession } from "../types";
import { ActionButton, Field, NumberInput, SectionTitle, SelectInput, TextInput } from "./ui";

type SessionFormPanelProps = {
  classroomCode: string;
  activeSessions: LocalSession[];
  courses: string[];
  divisions: string[];
  exams: ExamOption[];
  formErrors: FormErrors;
  expectedStudentCount: number;
  isBusy: boolean;
  isLoadingExams: boolean;
  operatorName: string;
  resumeAccessCode: string;
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
  onRefreshExams: () => void;
  onResumeAccessCodeChange: (value: string) => void;
  onResumeSession: (accessCode?: string) => void;
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

      <Field label="Examen" error={props.formErrors.selectedExamId}>
        <SelectInput
          value={props.selectedExamId}
          options={examOptions}
          emptyLabel={props.isLoadingExams ? "Cargando examenes..." : "Sin examenes disponibles"}
          onChange={props.onSelectedExamChange}
        />
      </Field>

      <div className="form-grid">
        <Field label="CUE" error={props.formErrors.cue}>
          <TextInput value={props.schoolCode} readOnly onChange={() => undefined} />
        </Field>

        <Field label="Escuela">
          <TextInput value={props.schoolName} readOnly onChange={() => undefined} />
        </Field>
      </div>

      <div className="form-grid">
        <Field label="Curso y division de la toma" error={props.formErrors.classroomCode}>
          <TextInput value={props.classroomCode} onChange={props.onClassroomCodeChange} />
        </Field>

        <Field label="Alumnos esperados" error={props.formErrors.expectedStudentCount}>
          <NumberInput
            min={1}
            max={500}
            value={props.expectedStudentCount}
            onChange={props.onExpectedStudentCountChange}
          />
        </Field>
      </div>

      <Field label="Operador" error={props.formErrors.operatorName}>
        <TextInput value={props.operatorName} onChange={props.onOperatorNameChange} />
      </Field>

      {props.exams.length === 0 && !props.isLoadingExams && (
        <div className="empty-state">
          <strong>No hay examenes locales.</strong>
          <span>Sincroniza o publica un examen en este equipo antes de crear una toma.</span>
          <ActionButton variant="secondary" disabled={props.isBusy} onClick={props.onRefreshExams}>
            Reintentar carga
          </ActionButton>
        </div>
      )}

      <ActionButton disabled={props.isBusy || props.isLoadingExams || !props.selectedExamId} onClick={props.onCreateSession}>
        {props.isBusy ? "Creando sesion..." : "Crear sesion de toma"}
      </ActionButton>

      <div className="resume-box">
        <SectionTitle
          title="Retomar una sesion"
          description="Usa un codigo activo para reabrir una toma anterior en este equipo."
        />
        {props.activeSessions.length > 0 && (
          <div className="session-list">
            {props.activeSessions.slice(0, 4).map(session => (
              <button key={session.id} type="button" onClick={() => props.onResumeSession(session.accessCode)}>
                <strong>{session.accessCode}</strong>
                <span>{session.schoolCode} - {session.classroomCode ?? "Sin aula"} - {session.status}</span>
              </button>
            ))}
          </div>
        )}
        <Field label="Codigo de sesion" error={props.formErrors.accessCode}>
          <TextInput value={props.resumeAccessCode} onChange={props.onResumeAccessCodeChange} />
        </Field>
        <ActionButton variant="secondary" disabled={props.isBusy} onClick={() => props.onResumeSession()}>
          Reabrir por codigo
        </ActionButton>
      </div>
    </section>
  );
}
