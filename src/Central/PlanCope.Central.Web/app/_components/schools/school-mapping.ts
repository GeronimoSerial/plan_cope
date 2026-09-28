import { canCreateSchool, canEditSchool, normalizeCue } from "../../_lib/scope";
import type { UserProfile } from "../../_lib/contracts";

export interface SchoolCreateFormValues {
  cue: string;
  code: string;
  name: string;
  localityId: string;
  annex?: string | number | null;
}

export interface SchoolCreatePayload {
  cue: string;
  code: string;
  name: string;
  localityId: string;
  annex: number | null;
}

export interface SchoolUpdateFormValues {
  name: string;
  localityId: string;
  annex?: string | number | null;
}

export interface SchoolUpdatePayload {
  name: string;
  annex: number | null;
  localityId: string;
}

function toAnnex(value: string | number | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const text = typeof value === "number" ? String(value) : value.trim();
  if (text === "") {
    return null;
  }
  const parsed = Number(text);
  return Number.isNaN(parsed) ? null : parsed;
}

export function buildCreateSchoolPayload(values: SchoolCreateFormValues): SchoolCreatePayload {
  return {
    cue: normalizeCue(values.cue) ?? values.cue,
    code: values.code,
    name: values.name,
    localityId: values.localityId,
    annex: toAnnex(values.annex)
  };
}

export function buildUpdateSchoolPayload(values: SchoolUpdateFormValues): SchoolUpdatePayload {
  return {
    name: values.name,
    annex: toAnnex(values.annex),
    localityId: values.localityId
  };
}

export function formatAnnex(annex: number | null | undefined): string {
  return annex === null || annex === undefined ? "—" : String(annex);
}

export function canOfferSchoolCreate(user: Pick<UserProfile, "role" | "rosterScope">): boolean {
  return canCreateSchool(user);
}

export function canOfferSchoolEdit(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  cue: string
): boolean {
  return canEditSchool(user, cue);
}

export function schoolsEmptyStateDescription(user: Pick<UserProfile, "role" | "rosterScope">): string {
  return canCreateSchool(user) ? "Creá una escuela para empezar." : "No hay escuelas para tu alcance.";
}
