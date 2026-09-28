import type { UserProfile } from "./contracts";

export const CUE_LENGTH = 9;

// Espejo de PlanCope.Shared.Domain.ValueObjects.CueCode.TryNormalize: solo digitos, guiones y
// espacios en blanco; todo lo demas invalida el CUE. Se quitan los no-digitos y deben quedar
// exactamente CUE_LENGTH digitos.
const CUE_ALLOWED_CHARS = /^[\d\s-]+$/;
const NON_DIGIT = /\D/g;

/** Returns the normalized 9-digit CUE, or null if the input does not normalize. */
export function normalizeCue(value: string): string | null {
  if (!CUE_ALLOWED_CHARS.test(value)) {
    return null;
  }
  const digits = value.replace(NON_DIGIT, "");
  return digits.length === CUE_LENGTH ? digits : null;
}

/** role === "Admin" OR rosterScope === "province". */
export function hasUnboundedScope(user: Pick<UserProfile, "role" | "rosterScope">): boolean {
  return user.role === "Admin" || user.rosterScope === "province";
}

/** hasUnboundedScope(user) OR (rosterScope === "school" AND cue is in rosterCues, both normalized). */
export function hasCueScope(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  cue: string
): boolean {
  if (hasUnboundedScope(user)) {
    return true;
  }
  if (user.rosterScope !== "school") {
    return false;
  }
  const normalized = normalizeCue(cue);
  if (normalized === null) {
    return false;
  }
  return user.rosterCues.some((entry) => normalizeCue(entry) === normalized);
}

export function canCreateSchool(user: Pick<UserProfile, "role" | "rosterScope">): boolean {
  return hasUnboundedScope(user);
}

export function canEditSchool(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  schoolCue: string
): boolean {
  return hasCueScope(user, schoolCue);
}

export function canCreateUser(user: Pick<UserProfile, "role" | "rosterScope">): boolean {
  return hasUnboundedScope(user);
}

/** targetUserCues = the CUEs currently assigned to the target user (UserSummaryDto.cues). */
export function canManageUser(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetUserCues: readonly string[]
): boolean {
  if (hasUnboundedScope(user)) {
    return true;
  }
  return targetUserCues.some((cue) => hasCueScope(user, cue));
}

export const UNBOUNDED_SCOPE_ROLES: ReadonlySet<string> = new Set(["Admin", "RosterProvince"]);

export function canAssignRole(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetUserCues: readonly string[],
  roleCode: string
): boolean {
  // canManageUser AND (roleCode not in UNBOUNDED_SCOPE_ROLES OR hasUnboundedScope)
  if (!canManageUser(user, targetUserCues)) {
    return false;
  }
  if (UNBOUNDED_SCOPE_ROLES.has(roleCode) && !hasUnboundedScope(user)) {
    return false;
  }
  return true;
}

export function canRevokeRole(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetUserCues: readonly string[]
): boolean {
  return canManageUser(user, targetUserCues);
}

export function canAssignOrRevokeCue(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  cue: string
): boolean {
  return hasCueScope(user, cue);
}
