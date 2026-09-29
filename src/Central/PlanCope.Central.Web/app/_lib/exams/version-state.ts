import type { ExamVersion } from "../contracts";
import type { GlossaryTerm } from "../glossary";

// Helpers puros para razonar sobre el ciclo de vida de las versiones de un examen:
// borrador -> publicada (actual) -> reemplazada. Sin dependencias de React.
//
// Regla de negocio (contracto con el API): una version publicada es inmutable. Editar un examen
// publicado crea una version nueva que arranca como copia de otra y, al publicarse, reemplaza a la
// version publicada anterior. La ultima publicada es la "actual"; las anteriores quedan
// "reemplazadas".

export type VersionStatusLabel = "Borrador" | "Publicada" | "Reemplazada";

type VersionStatusInput = { status: string | null | undefined; isCurrent?: boolean | null };

function isPublishedStatus(status: string | null | undefined): boolean {
  return (status ?? "").trim().toLowerCase() === "published";
}

export function versionStatusLabel(version: VersionStatusInput): VersionStatusLabel {
  if (!isPublishedStatus(version.status)) {
    return "Borrador";
  }
  return version.isCurrent ? "Publicada" : "Reemplazada";
}

export function versionStatusTerm(version: VersionStatusInput): GlossaryTerm {
  if (!isPublishedStatus(version.status)) {
    return "estado-borrador";
  }
  return version.isCurrent ? "estado-publicado" : "estado-reemplazada";
}

export function versionStatusBadgeVariant(
  version: VersionStatusInput
): "default" | "secondary" | "outline" {
  if (!isPublishedStatus(version.status)) {
    return "secondary";
  }
  return version.isCurrent ? "default" : "outline";
}

/** Numero que tendra la proxima version del examen (maximo + 1, minimo 1). */
export function nextVersionNumber(versions: ExamVersion[]): number {
  const max = versions.reduce((acc, version) => Math.max(acc, version.versionNumber), 0);
  return max + 1;
}

/** El borrador del examen, si existe. Si hubiera mas de uno, devuelve el de numero mayor. */
export function findDraft(versions: ExamVersion[]): ExamVersion | undefined {
  return versions
    .filter(version => !isPublishedStatus(version.status))
    .sort((a, b) => b.versionNumber - a.versionNumber)[0];
}

/** La version publicada actual. Cae a la publicada de numero mayor si `isCurrent` no llega. */
export function findCurrent(versions: ExamVersion[]): ExamVersion | undefined {
  const published = versions.filter(version => isPublishedStatus(version.status));
  return published.find(version => version.isCurrent) ??
    published.sort((a, b) => b.versionNumber - a.versionNumber)[0];
}

export type PrimaryEditTarget =
  | { kind: "open-draft"; versionId: string }
  | { kind: "create-from"; sourceVersionId: string; sourceNumber: number; nextNumber: number }
  | { kind: "none" };

/**
 * A donde debe llevar la accion principal "Editar" del examen:
 * - hay borrador -> abrir ese borrador (nunca crear un segundo en silencio);
 * - no hay borrador y hay publicada -> crear una copia de la publicada actual;
 * - sin versiones -> nada que editar.
 */
export function primaryEditTarget(versions: ExamVersion[]): PrimaryEditTarget {
  const draft = findDraft(versions);
  if (draft) {
    return { kind: "open-draft", versionId: draft.id };
  }
  const current = findCurrent(versions);
  if (current) {
    return {
      kind: "create-from",
      sourceVersionId: current.id,
      sourceNumber: current.versionNumber,
      nextNumber: nextVersionNumber(versions)
    };
  }
  return { kind: "none" };
}

/** Linea de estado del builder: "Versión N · Borrador (basada en la versión M)". */
export function versionStatusLine(input: {
  versionNumber: number;
  status: string | null | undefined;
  isCurrent?: boolean | null;
  basedOnVersionNumber?: number | null;
}): string {
  const label = versionStatusLabel(input);
  const based =
    label === "Borrador" && input.basedOnVersionNumber != null
      ? ` (basada en la versión ${input.basedOnVersionNumber})`
      : "";
  return `Versión ${input.versionNumber} · ${label}${based}`;
}

/** Texto de confirmacion al crear una version copiada de otra. */
export function createVersionConfirmation(input: {
  nextNumber: number;
  sourceNumber: number;
  sourcePublished: boolean;
}): string {
  const first = input.sourcePublished
    ? `Se va a crear la versión ${input.nextNumber} a partir de la versión ${input.sourceNumber} publicada.`
    : `Se va a crear la versión ${input.nextNumber} a partir de la versión ${input.sourceNumber}.`;
  const second = input.sourcePublished
    ? "La versión publicada no cambia hasta que publiques la nueva."
    : `La versión ${input.sourceNumber} no cambia hasta que publiques la nueva.`;
  return `${first} ${second}`;
}

/** Ruta del builder que abre una version para editar. */
export function versionBuilderHref(examId: string, versionId: string): string {
  return `/exams/${encodeURIComponent(examId)}/versions/${encodeURIComponent(versionId)}/builder`;
}

export interface DraftExistsConflict {
  draftVersionId: string;
}

/**
 * Reconoce el conflicto 409 `draft_exists` que devuelve el API al crear una version cuando ya hay
 * un borrador. El cuerpo trae `draftVersionId`, que es el borrador a abrir. Acepta cualquier error
 * con `status` y `body` (el `CentralApiError` del cliente) para no acoplar este helper al fetch.
 */
export function draftExistsConflict(error: unknown): DraftExistsConflict | null {
  if (typeof error !== "object" || error === null) {
    return null;
  }

  const candidate = error as { status?: unknown; body?: unknown };
  if (candidate.status !== 409) {
    return null;
  }

  const body = candidate.body;
  if (typeof body !== "object" || body === null) {
    return null;
  }

  const { code, draftVersionId } = body as { code?: unknown; draftVersionId?: unknown };
  if (code !== "draft_exists" || typeof draftVersionId !== "string" || draftVersionId.trim().length === 0) {
    return null;
  }

  return { draftVersionId };
}

/**
 * Aviso de reemplazo en el dialogo de publicacion: solo aparece cuando ya hay una version
 * publicada actual de numero menor a la que se esta publicando.
 */
export function publishSupersedeMessage(input: {
  currentPublishedNumber?: number | null;
  versionNumber: number;
}): string | null {
  const current = input.currentPublishedNumber;
  if (current == null || current >= input.versionNumber) {
    return null;
  }
  return `Reemplaza a la versión ${current} en los nodos.`;
}
