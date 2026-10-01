import {
  canAssignOrRevokeCue,
  canAssignRole,
  canCreateUser,
  canManageUser,
  canRevokeRole,
  hasUnboundedScope,
  normalizeCue
} from "../../_lib/scope";
import type { UserProfile } from "../../_lib/contracts";
import type { RoleSummary } from "../../_lib/api/server";

// Espejo de la validacion del backend (MinPasswordLength): el servidor rechaza passwords de
// menos de 8 caracteres con 400, pero validamos antes del round-trip.
export const MIN_PASSWORD_LENGTH = 8;

export interface UserCreateFormValues {
  email: string;
  password: string;
  fullName: string;
}

export interface UserCreatePayload {
  email: string;
  password: string;
  fullName: string;
}

export function buildCreateUserPayload(values: UserCreateFormValues): UserCreatePayload {
  return {
    email: values.email,
    password: values.password,
    fullName: values.fullName
  };
}

export function formatCues(cues: string[]): string {
  return cues.length === 0 ? "Todas las escuelas" : cues.join(", ");
}

export function formatRoleCodes(roleCodes: string[]): string {
  return roleCodes.length === 0 ? "—" : roleCodes.join(", ");
}

export { roleLabel, roleTerm } from "../../_lib/roles";

/** Etiqueta en español para el estado del usuario que expone el backend (Active/Inactive). */
export function userStatusLabel(status: string): string {
  switch (status.toLowerCase()) {
    case "active":
      return "Activo";
    case "inactive":
      return "Inactivo";
    default:
      return status;
  }
}

export function canOfferUserCreate(user: Pick<UserProfile, "role" | "rosterScope">): boolean {
  return canCreateUser(user);
}

export function canOfferUserDeactivate(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetCues: readonly string[],
  targetStatus: string
): boolean {
  // Espejo de IsActive del backend: comparacion literal, sin normalizar.
  return canManageUser(user, targetCues) && targetStatus === "Active";
}

export function canOfferResetPassword(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetCues: readonly string[]
): boolean {
  return canManageUser(user, targetCues);
}

export function usersEmptyStateDescription(user: Pick<UserProfile, "role" | "rosterScope">): string {
  return canCreateUser(user) ? "Creá un usuario para empezar." : "No hay usuarios para tu alcance.";
}

/** Role codes the caller may assign to this target right now: not already assigned, and allowed by canAssignRole. */
export function rolesAvailableToAssign(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetCues: readonly string[],
  targetRoleCodes: readonly string[],
  roles: readonly Pick<RoleSummary, "code">[]
): string[] {
  return roles
    .map(role => role.code)
    .filter(code => !targetRoleCodes.includes(code) && canAssignRole(user, targetCues, code));
}

export function canOfferRoleRevoke(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetCues: readonly string[],
  roleCode: string
): boolean {
  return canRevokeRole(user, targetCues, roleCode);
}

export function canOfferCueRevoke(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  cue: string
): boolean {
  return canAssignOrRevokeCue(user, cue);
}

/**
 * CUE options the caller may offer to assign to this target:
 * - null  => caller has unbounded scope: the UI should offer a free-text CUE input (any CUE is valid to try).
 * - array => caller is school-scope: exactly the caller's own CUEs not already assigned to the target
 *            (normalized, deduped). An empty array means there is nothing left to offer — hide the control.
 */
export function cuesAvailableToAssign(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  targetCues: readonly string[]
): string[] | null {
  if (hasUnboundedScope(user)) {
    return null;
  }
  if (user.rosterScope !== "school") {
    return [];
  }
  const targetNormalized = new Set(targetCues.map(cue => normalizeCue(cue) ?? cue));
  const options = new Set<string>();
  for (const cue of user.rosterCues) {
    const normalized = normalizeCue(cue) ?? cue;
    if (!targetNormalized.has(normalized)) {
      options.add(normalized);
    }
  }
  return [...options];
}

/** Whether the row's "Roles y escuelas" management panel has anything at all to show/offer — used to decide whether to render the toggle button in the first place. */
export function hasAnyRoleOrCueAction(
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">,
  target: { cues: readonly string[]; roleCodes: readonly string[] },
  roles: readonly Pick<RoleSummary, "code">[]
): boolean {
  if (target.roleCodes.some(code => canOfferRoleRevoke(user, target.cues, code))) {
    return true;
  }
  if (rolesAvailableToAssign(user, target.cues, target.roleCodes, roles).length > 0) {
    return true;
  }
  if (target.cues.some(cue => canOfferCueRevoke(user, cue))) {
    return true;
  }
  const cueOptions = cuesAvailableToAssign(user, target.cues);
  return cueOptions === null || cueOptions.length > 0;
}
