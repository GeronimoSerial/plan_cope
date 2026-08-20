import type { ExamOption, FormErrors } from "../types";
import type { RosterSection, RosterSnapshot } from "../types";
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
  onCourseChange: (value: string) => void;
  onCreateSession: () => void;
  onDivisionChange: (value: string) => void;
  onExpectedStudentCountChange: (value: number) => void;
  onOperatorNameChange: (value: string) => void;
  onRefreshExams: () => void;
  onSelectedExamChange: (value: string) => void;
  roster: {
    schoolYear: string;
    setSchoolYear: (value: string) => void;
    snapshot: RosterSnapshot | null;
    sections: RosterSection[];
    selectedSectionId: string;
    setSelectedSectionId: (value: string) => void;
    isLoading: boolean;
    isPulling: boolean;
    error: string | null;
    refresh: () => void;
  };
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
  onCourseChange,
  onCreateSession,
  onDivisionChange,
  onExpectedStudentCountChange,
  onOperatorNameChange,
  onRefreshExams,
  onSelectedExamChange,
  roster
}: SessionCreatePanelProps) {
  const examOptions = exams.map(exam => ({ value: exam.id, label: exam.displayName }));
  const schoolYearOptions = Array.from({ length: 3 }, (_, index) => String(new Date().getFullYear() - 1 + index));
  const rosterSectionOptions = roster.sections.map(section => ({
    value: section.id,
    label: `${section.course ?? "Sin curso"} · ${section.division ?? "Sin división"}${section.shift ? ` · ${section.shift}` : ""} (${section.studentCount} alumnos)`
  }));

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

      <div className="roster-panel" aria-labelledby="roster-title" aria-busy={roster.isLoading || roster.isPulling}>
        <div className="roster-panel-header">
          <div>
            <h3 id="roster-title">Padrón nominal GE</h3>
            <p>Se actualiza únicamente cuando el operador lo solicita.</p>
          </div>
          <ActionButton
            variant="secondary"
            disabled={roster.isPulling || roster.isLoading || !form.cue.trim()}
            onClick={roster.refresh}
          >
            {roster.isPulling ? "Actualizando padrón…" : "Actualizar padrón"}
          </ActionButton>
        </div>

        <div className="form-grid">
          <Field label="Ciclo lectivo">
            <SelectInput
              value={roster.schoolYear}
              options={schoolYearOptions.map(value => ({ value, label: value }))}
              onChange={roster.setSchoolYear}
            />
          </Field>
          <Field label="Sección GE" error={formErrors.rosterSectionId}>
            <SelectInput
              value={roster.selectedSectionId}
              options={rosterSectionOptions}
              emptyLabel={roster.isLoading ? "Consultando padrón…" : "Selecciona una sección"}
              onChange={roster.setSelectedSectionId}
            />
          </Field>
        </div>

        {roster.snapshot ? (
          <>
            <p className="roster-meta" role="status">
              Snapshot {roster.snapshot.status.toLowerCase()} · actualizado {new Date(roster.snapshot.fetchedAt).toLocaleString()} · {roster.snapshot.studentCount} alumnos en {roster.snapshot.sectionCount} secciones.
            </p>
            {roster.snapshot.status.toLowerCase() !== "ready" && (
              <p className="roster-meta">Este padrón no está disponible. Actualizá el padrón antes de crear la sesión.</p>
            )}
          </>
        ) : (
          <div className="empty-state roster-empty" role="status">
            <strong>{roster.isLoading ? "Consultando el padrón local…" : "No hay padrón local para este CUE y ciclo."}</strong>
            <span>Actualizá el padrón para habilitar la creación de una sesión nominal.</span>
          </div>
        )}
        {roster.error && <p className="error-banner" role="alert">{roster.error}</p>}
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
        <Field label="Curso y división GE" error={formErrors.classroomCode}>
          <TextInput
            value={roster.snapshot ? form.classroomCode || "Selecciona una sección GE" : "Actualizá el padrón para seleccionar"}
            readOnly
            onChange={() => undefined}
          />
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

      <ActionButton
        disabled={
          isBusy ||
          isLoadingExams ||
          roster.isLoading ||
          roster.isPulling ||
          !selectedExamId ||
          !roster.snapshot ||
          roster.snapshot.status.toLowerCase() !== "ready" ||
          !roster.selectedSectionId
        }
        onClick={onCreateSession}
      >
        {isBusy ? "Creando sesion..." : "Crear sesion de toma"}
      </ActionButton>
    </section>
  );
}
