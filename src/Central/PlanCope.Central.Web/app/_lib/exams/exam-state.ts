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

export function formatReceivedBy(count: number | null | undefined): string {
  if (count === null || count === undefined) {
    return "";
  }
  if (count <= 0) {
    return "Ningún nodo lo recibió todavía";
  }
  if (count === 1) {
    return "Recibido por 1 nodo";
  }
  return `Recibido por ${count} nodos`;
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
  return exams.filter(
    exam => normalizeForSearch(exam.title).includes(term) || normalizeForSearch(exam.code).includes(term)
  );
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
  code: string;
  title: string;
}

export interface CreateExamErrors {
  code?: string;
  title?: string;
}

const examCodePattern = /^[\p{L}\p{N}_-]+$/u;

// Espeja ExamCode.IsValid en el backend: no vacío, hasta 64 caracteres,
// solo letras/dígitos (Unicode), guiones y guiones bajos.
export function isValidExamCode(code: string): boolean {
  const trimmed = code.trim();
  return trimmed.length > 0 && trimmed.length <= 64 && examCodePattern.test(trimmed);
}

export function validateCreateExam(input: CreateExamInput): CreateExamErrors {
  const errors: CreateExamErrors = {};
  const code = input.code.trim();
  const title = input.title.trim();

  if (!code) {
    errors.code = "El código es requerido.";
  } else if (code.length > 64) {
    errors.code = "El código no puede superar los 64 caracteres.";
  } else if (!isValidExamCode(code)) {
    errors.code = "El código solo admite letras, números, guiones y guiones bajos.";
  }

  if (!title) {
    errors.title = "El título es requerido.";
  } else if (title.length > 256) {
    errors.title = "El título no puede superar los 256 caracteres.";
  }

  return errors;
}
