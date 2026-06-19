import type { ExamMetadata, LocalExam } from "../../shared/api-types";
import type { ExamOption } from "../types";

export function toExamOption(exam: LocalExam): ExamOption {
  const metadata = parseMetadata(exam.metadataJson);
  const course = metadata.grade ?? metadata.course ?? metadata.curso;
  const division = metadata.division ?? metadata.section ?? metadata.classroom;
  const title = metadata.title ?? exam.examCode;
  const group = [course, division].filter(Boolean).join(" ");

  return {
    ...exam,
    title,
    course,
    division,
    displayName: group ? `${title} - ${group} v${exam.versionNumber}` : `${title} v${exam.versionNumber}`
  };
}

export function uniqueSorted(values: Array<string | undefined>): string[] {
  return [...new Set(values.filter(Boolean) as string[])].sort((a, b) => a.localeCompare(b, "es"));
}

export function filterExams(exams: ExamOption[], selectedCourse: string, selectedDivision: string): ExamOption[] {
  return exams
    .filter(exam => !selectedCourse || exam.course === selectedCourse)
    .filter(exam => !selectedDivision || exam.division === selectedDivision);
}

export function ensureSelectedExamId(exams: ExamOption[], currentId: string): string {
  if (exams.some(exam => exam.id === currentId)) {
    return currentId;
  }

  return exams[0]?.id ?? "";
}

function parseMetadata(metadataJson?: string | null): ExamMetadata {
  if (!metadataJson) {
    return {};
  }

  try {
    return JSON.parse(metadataJson) as ExamMetadata;
  } catch {
    return {};
  }
}
