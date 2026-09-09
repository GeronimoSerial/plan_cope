import type { ExamOption, FormErrors } from "../types";
import type { RosterSection, RosterSnapshot } from "../types";
import { ActionButton, Field, SectionTitle, SelectInput } from "../../shared/ui";

type SessionCreatePanelProps = {
  exams: ExamOption[];
  formErrors: FormErrors;
  selectedExamId: string;
  isBusy: boolean;
  isLoadingExams: boolean;
  onCreateSession: () => void;
  onRefreshExams: () => void;
  onSelectedExamChange: (value: string) => void;
  roster: {
    snapshot: RosterSnapshot | null;
    sections: RosterSection[];
    selectedSectionId: string;
    setSelectedSectionId: (value: string) => void;
    isLoading: boolean;
    error: string | null;
  };
};

export function SessionCreatePanel({
  exams,
  formErrors,
  selectedExamId,
  isBusy,
  isLoadingExams,
  onCreateSession,
  onRefreshExams,
  onSelectedExamChange,
  roster
}: SessionCreatePanelProps) {
  const examOptions = exams.map(exam => ({ value: exam.id, label: exam.displayName }));
  const rosterSectionOptions = roster.sections.map(section => ({
    value: section.id,
    label: sectionLabel(section)
  }));
  const rosterReady = roster.snapshot?.status.toLowerCase() === "ready";
  const selectedSection = roster.sections.find(section => section.id === roster.selectedSectionId);

  return (
    <section className="panel">
      <SectionTitle
        title="Nueva sesión"
        description="Elegí una sección y un examen."
      />

      <div className="form-grid">
        <Field label="Sección" error={formErrors.rosterSectionId}>
          <SelectInput
            value={roster.selectedSectionId}
            options={rosterSectionOptions}
            emptyLabel={roster.isLoading ? "Consultando padrón…" : "Elegí una sección"}
            onChange={roster.setSelectedSectionId}
          />
        </Field>

        <Field label="Examen" error={formErrors.selectedExamId}>
          <SelectInput
            value={selectedExamId}
            options={examOptions}
            emptyLabel={isLoadingExams ? "Cargando exámenes…" : "Sin exámenes disponibles"}
            onChange={onSelectedExamChange}
          />
        </Field>
      </div>

      {selectedSection && (
        <p className="selection-summary" role="status">
          <span>{sectionDetails(selectedSection)}</span>
        </p>
      )}

      {roster.snapshot && (
        <p className="roster-meta">
          Padrón {roster.snapshot.schoolYear} · actualizado {new Date(roster.snapshot.fetchedAt).toLocaleDateString("es-AR")}
        </p>
      )}
      {roster.error && <p className="error-banner" role="alert">{roster.error}</p>}

      {exams.length === 0 && !isLoadingExams && (
        <div className="empty-state">
          <strong>No hay exámenes locales.</strong>
          <span>Sincronizá o publicá un examen en este equipo para crear una sesión.</span>
          <ActionButton variant="secondary" disabled={isBusy} onClick={onRefreshExams}>
            Reintentar
          </ActionButton>
        </div>
      )}

      <ActionButton
        disabled={
          isBusy ||
          isLoadingExams ||
          roster.isLoading ||
          !selectedExamId ||
          !rosterReady ||
          !roster.selectedSectionId
        }
        onClick={onCreateSession}
      >
        {isBusy ? "Creando sesión…" : "Crear sesión"}
      </ActionButton>
    </section>
  );
}

function sectionName(section: RosterSection): string {
  return [section.course, section.division].filter(Boolean).join(" ") || "Sección sin nombre";
}

function sectionDetails(section: RosterSection): string {
  return [section.level, section.shift && `Turno ${section.shift}`, `${section.studentCount} estudiantes`]
    .filter(Boolean)
    .join(" · ");
}

function sectionLabel(section: RosterSection): string {
  return sectionName(section);
}
