import type { CreateSessionRequest, ExamOption, FormErrors, RosterSection, RosterSnapshot } from "../types";
import { isValidCue } from "./cue";

export type SessionForm = {
  cue: string;
  classroomCode: string;
  operatorName: string;
  expectedStudentCount: number;
};

export function initialSessionForm(operatorName: string): SessionForm {
  return {
    cue: "",
    classroomCode: "",
    operatorName,
    expectedStudentCount: 0
  };
}

export function validateSessionForm(form: SessionForm, selectedExamId: string): FormErrors {
  const errors: FormErrors = {};

  if (!isValidCue(form.cue)) {
    errors.cue = "El CUE debe tener exactamente 9 dígitos.";
  }

  if (!selectedExamId) {
    errors.selectedExamId = "Selecciona un examen disponible.";
  }

  if (!form.classroomCode.trim()) {
    errors.classroomCode = "Completa el curso y division.";
  }

  if (!form.operatorName.trim()) {
    errors.operatorName = "Completa el operador.";
  }

  if (!Number.isFinite(form.expectedStudentCount) || form.expectedStudentCount < 1 || form.expectedStudentCount > 500) {
    errors.expectedStudentCount = "Debe estar entre 1 y 500.";
  }

  return errors;
}

export function buildCreateSessionRequest(
  form: SessionForm,
  exam: ExamOption,
  snapshot?: RosterSnapshot | null,
  section?: RosterSection | null
): CreateSessionRequest {
  return {
    examVersionId: exam.id,
    schoolCode: form.cue.trim(),
    classroomCode: form.classroomCode.trim(),
    commissionCode: null,
    startedBy: form.operatorName.trim(),
    expectedStudentCount: form.expectedStudentCount,
    config: null,
    schoolYear: snapshot?.schoolYear ?? null,
    rosterSnapshotId: snapshot?.id ?? null,
    rosterSectionId: section?.id ?? null
  };
}
