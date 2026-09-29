import type { PublishExamVersionRequest } from "../contracts";

// Pure helpers for the publish dialog: request building, validation and the lazy
// school/node pickers. Row shapes mirror the Central read endpoints but live here so the
// browser-side module does not depend on server helpers.

export type PublishTargetMode = "all" | "schools" | "nodes";

// GET /api/admin/schools
export interface SchoolTargetRow {
  id: string;
  cue: string;
  code: string;
  name: string;
  localityId: string;
  annex?: number | null;
  status: string;
}

// GET /api/admin/activation/nodes
export interface NodeTargetRow {
  id: string;
  nodeCode: string;
  cue: string;
  deviceName?: string | null;
  schoolName?: string | null;
  revokedAt?: string | null;
}

export interface BuildPublishRequestInput {
  grade: string;
  subject?: string | null;
  mode: PublishTargetMode;
  /** School CUEs (the API accepts a School id or its CUE; we always send the CUE). */
  schoolIds?: readonly string[];
  nodeIds?: readonly string[];
}

export const TARGET_RESULT_LIMIT = 20;

function cleanIds(ids: readonly string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const id of ids) {
    const trimmed = id.trim();
    if (trimmed && !seen.has(trimmed)) {
      seen.add(trimmed);
      result.push(trimmed);
    }
  }
  return result;
}

function optionalText(value: string | null | undefined): string | null {
  const trimmed = (value ?? "").trim();
  return trimmed.length > 0 ? trimmed : null;
}

/**
 * Builds the publish payload. "all" omits both nodeIds and schoolIds (the API treats a missing
 * or empty filter as "every node"); "schools" sends only schoolIds (CUEs); "nodes" sends only
 * nodeIds. Empty selections are omitted instead of sent as empty arrays.
 */
export function buildPublishRequest(input: BuildPublishRequestInput): PublishExamVersionRequest {
  const request: PublishExamVersionRequest = {
    grade: input.grade.trim(),
    subject: optionalText(input.subject)
  };

  if (input.mode === "schools") {
    const schoolIds = cleanIds(input.schoolIds ?? []);
    if (schoolIds.length > 0) {
      request.schoolIds = schoolIds;
    }
  } else if (input.mode === "nodes") {
    const nodeIds = cleanIds(input.nodeIds ?? []);
    if (nodeIds.length > 0) {
      request.nodeIds = nodeIds;
    }
  }

  return request;
}

export interface PublishTargetSelection {
  grade: string;
  mode: PublishTargetMode;
  schoolIds: readonly string[];
  nodeIds: readonly string[];
}

export type PublishValidationField = "grade" | "targets";

export interface PublishTargetValidationError {
  field: PublishValidationField;
  message: string;
}

/** Returns the first validation error, or null when the selection is valid. */
export function validatePublishTargets(selection: PublishTargetSelection): PublishTargetValidationError | null {
  if (selection.grade.trim().length === 0) {
    return { field: "grade", message: "Ingresá el curso o grado." };
  }
  if (selection.mode === "schools" && cleanIds(selection.schoolIds).length === 0) {
    return { field: "targets", message: "Elegí al menos una escuela." };
  }
  if (selection.mode === "nodes" && cleanIds(selection.nodeIds).length === 0) {
    return { field: "targets", message: "Elegí al menos un nodo." };
  }
  return null;
}

export interface TargetSearchResult<T> {
  /** Rows to render (already capped to `limit`). */
  rows: T[];
  /** Total matches before the cap. */
  total: number;
  /** Number of rows actually returned. */
  shown: number;
  /** True when `total` exceeds the cap and the list was truncated. */
  truncated: boolean;
}

// Caps a match list and reports whether it was truncated, so the dialog can tell the user to
// narrow the search instead of silently hiding results.
export function limitTargetResults<T>(rows: readonly T[], limit: number = TARGET_RESULT_LIMIT): TargetSearchResult<T> {
  const rows_ = rows.slice(0, Math.max(0, limit));
  return { rows: rows_, total: rows.length, shown: rows_.length, truncated: rows.length > rows_.length };
}

/** Client-side school search by CUE or name, capped so we never render thousands of rows. */
export function searchSchools(
  rows: readonly SchoolTargetRow[],
  query: string,
  limit: number = TARGET_RESULT_LIMIT
): TargetSearchResult<SchoolTargetRow> {
  const needle = query.trim().toLowerCase();
  const matches = needle
    ? rows.filter(row => row.cue.toLowerCase().includes(needle) || row.name.toLowerCase().includes(needle))
    : rows;
  return limitTargetResults(matches, limit);
}

/** Client-side node search (nodeCode, CUE, device or school name), hiding revoked nodes. */
export function searchNodes(
  rows: readonly NodeTargetRow[],
  query: string,
  limit: number = TARGET_RESULT_LIMIT
): TargetSearchResult<NodeTargetRow> {
  const active = rows.filter(row => !row.revokedAt);
  const needle = query.trim().toLowerCase();
  const matches = needle
    ? active.filter(row =>
        [row.nodeCode, row.cue, row.deviceName ?? "", row.schoolName ?? ""].some(value =>
          value.toLowerCase().includes(needle)
        )
      )
    : active;
  return limitTargetResults(matches, limit);
}

function joinLimited(values: readonly string[], limit = 3): string {
  const shown = values.slice(0, limit).join(", ");
  return values.length > limit ? `${shown}…` : shown;
}

/** Compact, one-line description of where the package will be delivered. */
export function summarizeTargets(
  mode: PublishTargetMode,
  schools: readonly SchoolTargetRow[],
  nodes: readonly NodeTargetRow[]
): string {
  if (mode === "all") {
    return "Se entrega a: todas las escuelas";
  }
  if (mode === "schools") {
    if (schools.length === 0) {
      return "Sin escuelas seleccionadas";
    }
    const label = schools.length === 1 ? "escuela" : "escuelas";
    return `${schools.length} ${label}: CUE ${joinLimited(schools.map(school => school.cue))}`;
  }
  if (nodes.length === 0) {
    return "Sin nodos seleccionados";
  }
  const label = nodes.length === 1 ? "nodo" : "nodos";
  return `${nodes.length} ${label}: ${joinLimited(nodes.map(node => node.nodeCode))}`;
}
