import type { ExamSummary, PublishBlockedReason, PublicationState } from "../contracts";
import type { GlossaryTerm } from "../glossary";

// Helpers puros para traducir el estado de publicación del API a etiquetas en español,
// filtrar la lista de exámenes y armar los mensajes de la UI. Sin dependencias de React.

export type BadgeVariant = "default" | "secondary" | "outline";

export function publicationStateLabel(state: PublicationState | string | null | undefined): string {
  switch (state) {
    case "ready_to_publish":
      return "Listo para publicar";
    case "published":
      return "Publicado";
    case "draft":
    default:
      return "Borrador";
  }
}

export function publicationStateBadgeVariant(state: PublicationState | string | null | undefined): BadgeVariant {
  switch (state) {
    case "published":
      return "default";
    case "ready_to_publish":
      return "outline";
    case "draft":
    default:
      return "secondary";
  }
}

export function versionStateLabel(status: string | null | undefined): string {
  const normalized = (status ?? "").trim().toLowerCase();
  if (normalized === "published") {
    return "Publicada";
  }
  return "Borrador";
}

/** Término del glosario para el estado de publicación de un examen. */
export function publicationStateTerm(state: PublicationState | string | null | undefined): GlossaryTerm {
  switch (state) {
    case "ready_to_publish":
      return "estado-listo-para-publicar";
    case "published":
      return "estado-publicado";
    case "draft":
    default:
      return "estado-borrador";
  }
}

/** Término del glosario para el estado de una versión. */
export function versionStateTerm(status: string | null | undefined): GlossaryTerm {
  const normalized = (status ?? "").trim().toLowerCase();
  return normalized === "published" ? "estado-publicado" : "estado-borrador";
}

const dateFormatter = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "America/Argentina/Buenos_Aires"
});

export function formatPublishedAt(value: string | null | undefined): string {
  if (!value) {
    return "";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }
  return dateFormatter.format(date);
}

function normalizeForSearch(value: string): string {
  return value
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
}

export function filterExams(exams: ExamSummary[], query: string): ExamSummary[] {
  const term = normalizeForSearch(query.trim());
  if (!term) {
    return exams;
  }
  return exams.filter(exam => normalizeForSearch(exam.title).includes(term));
}

export function publishBlockedMessage(reason: PublishBlockedReason | string | null | undefined): string {
  switch (reason) {
    case "already_published":
      return "Esta versión ya está publicada";
    case "no_blocks":
      return "Agregá al menos una pregunta";
    default:
      return "";
  }
}

export interface CreateExamInput {
  title: string;
}

export interface CreateExamErrors {
  title?: string;
}

export function validateCreateExam(input: CreateExamInput): CreateExamErrors {
  const errors: CreateExamErrors = {};
  const title = input.title.trim();

  if (!title) {
    errors.title = "El título es requerido.";
  } else if (title.length > 256) {
    errors.title = "El título no puede superar los 256 caracteres.";
  }

  return errors;
}
