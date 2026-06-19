import type { CreateSessionRequest, ExamOption, FormErrors } from "../types";

export type SessionForm = {
  cue: string;
  classroomCode: string;
  operatorName: string;
  expectedStudentCount: number;
};

export function initialSessionForm(operatorName: string): SessionForm {
  return {
    cue: "",
    classroomCode: "6A",
    operatorName,
    expectedStudentCount: 30
  };
}

export function validateSessionForm(form: SessionForm, selectedExamId: string): FormErrors {
  const errors: FormErrors = {};

  if (!form.cue.trim()) {
    errors.cue = "Completa el CUE.";
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

export function resolveSchoolName(cue: string): string {
  const normalizedCue = cue.trim().toUpperCase();
  if (!normalizedCue) {
    return "";
  }

  if (["ESCUELA-DEMO", "CUE-DEMO", "123456789"].includes(normalizedCue)) {
    return "Escuela Demo";
  }

  return `Escuela CUE ${normalizedCue}`;
}

export function buildCreateSessionRequest(form: SessionForm, exam: ExamOption): CreateSessionRequest {
  return {
    examVersionId: exam.id,
    schoolCode: form.cue.trim().toUpperCase(),
    classroomCode: form.classroomCode.trim(),
    commissionCode: null,
    startedBy: form.operatorName.trim(),
    expectedStudentCount: form.expectedStudentCount,
    config: null
  };
}
