// Maps publish failures to short Spanish messages. `callCentral` throws a plain Error whose
// message is the best line `extractApiError` could pull out of the Central response, so the
// adapter below reconstructs what it can (status, known validation keys) before mapping.

export interface PublishProblemDetails {
  errors?: Record<string, string[]>;
  detail?: string | null;
  title?: string | null;
}

export interface PublishErrorInput {
  status?: number | null;
  problem?: PublishProblemDetails | null;
  message?: string | null;
}

const GENERIC = "No se pudo publicar. Probá de nuevo.";
const ALREADY_PUBLISHED = "Esta versión ya está publicada.";
const NOT_FOUND = "No se encontró la versión.";
const NO_BLOCKS = "Agregá al menos una pregunta.";
const NO_POLICY = "Elegí una regla de puntaje.";
const NO_GRADE = "Ingresá el curso o grado.";

const NETWORK_MARKERS = ["failed to fetch", "load failed", "networkerror", "no se pudo conectar"];

// FluentValidation messages from ExamBlockValidator plus the publish gates.
const BLOCK_VALIDATION_MARKERS = [
  "at least one block",
  "scoring policy must be chosen",
  "grade/course is required",
  "requires a content string",
  "requires a question string",
  "requires at least two options",
  "requires a prompt string",
  "requires an assetid string",
  "reference assets that do not exist"
];

function firstValidationMessage(errors: Record<string, string[]> | undefined): string | null {
  if (!errors) {
    return null;
  }
  const values = Object.values(errors).flat().filter(value => value.trim().length > 0);
  return values[0] ?? null;
}

function hasErrorKey(errors: Record<string, string[]>, key: string): boolean {
  return Object.keys(errors).some(candidate => candidate.toLowerCase() === key.toLowerCase());
}

function isNetworkMessage(lower: string): boolean {
  return NETWORK_MARKERS.some(marker => lower.includes(marker));
}

function isBlockValidationMessage(lower: string): boolean {
  return BLOCK_VALIDATION_MARKERS.some(marker => lower.includes(marker));
}

/** Pure mapping from a structured publish error to the user-facing Spanish message. */
export function mapPublishError(input: PublishErrorInput): string {
  const status = input.status ?? null;
  const message = (input.message ?? "").trim();
  const lower = message.toLowerCase();
  const errors = input.problem?.errors;

  if (status === 409 || lower.includes("already published")) {
    return ALREADY_PUBLISHED;
  }
  if (status === 404 || lower.includes("estado 404") || lower.includes("not found")) {
    return NOT_FOUND;
  }
  if (status !== null && status >= 500) {
    return GENERIC;
  }
  if (isNetworkMessage(lower)) {
    return GENERIC;
  }

  if (errors) {
    if (hasErrorKey(errors, "blocks")) {
      return NO_BLOCKS;
    }
    if (hasErrorKey(errors, "scoringPolicy")) {
      return NO_POLICY;
    }
    if (hasErrorKey(errors, "grade")) {
      return NO_GRADE;
    }
    const validationMessage = firstValidationMessage(errors);
    if (validationMessage) {
      return `Revisá las preguntas: ${validationMessage}`;
    }
  }

  if (isBlockValidationMessage(lower)) {
    if (lower.includes("at least one block")) {
      return NO_BLOCKS;
    }
    if (lower.includes("scoring policy must be chosen")) {
      return NO_POLICY;
    }
    if (lower.includes("grade/course is required")) {
      return NO_GRADE;
    }
    return `Revisá las preguntas: ${message}`;
  }

  return GENERIC;
}

function detectStatus(lower: string): number | null {
  const explicit = lower.match(/estado\s+(\d{3})/);
  if (explicit) {
    return Number(explicit[1]);
  }
  if (lower.includes("already published")) {
    return 409;
  }
  if (lower.includes("not found") || lower.includes("no se encontró")) {
    return 404;
  }
  if (isNetworkMessage(lower)) {
    return 502;
  }
  return null;
}

/** Adapter for whatever `callCentral` threw (a plain Error/Caught value). */
export function parsePublishError(error: unknown): PublishErrorInput {
  let message: string | null = null;
  if (error instanceof Error) {
    message = error.message;
  } else if (typeof error === "string") {
    message = error;
  }

  const lower = (message ?? "").toLowerCase();
  return {
    status: message ? detectStatus(lower) : null,
    problem: null,
    message
  };
}

/** Convenience used by the dialog: parse a caught value and map it in one step. */
export function publishErrorMessage(error: unknown): string {
  return mapPublishError(parsePublishError(error));
}
