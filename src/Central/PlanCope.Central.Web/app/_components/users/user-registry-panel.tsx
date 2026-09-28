"use client";

import { Fragment, useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { normalizeCue } from "../../_lib/scope";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { ConfirmDialog } from "../ui/confirm-dialog";
import { EmptyState } from "../ui/empty-state";
import { StatusBadge } from "../ui/status-badge";
import { TextField } from "../ui/text-field";
import type { RoleSummary, UserSummary } from "../../_lib/api/server";
import type { UserProfile } from "../../_lib/contracts";
import {
  MIN_PASSWORD_LENGTH,
  buildCreateUserPayload,
  canOfferCueRevoke,
  canOfferResetPassword,
  canOfferRoleRevoke,
  canOfferUserCreate,
  canOfferUserDeactivate,
  cuesAvailableToAssign,
  formatCues,
  formatRoleCodes,
  hasAnyRoleOrCueAction,
  rolesAvailableToAssign,
  usersEmptyStateDescription
} from "./user-mapping";

interface UserRegistryPanelProps {
  initialUsers: UserSummary[];
  roles: RoleSummary[];
  user: UserProfile;
}

const createUserSchema = z.object({
  email: z.string().trim().min(1, "El email es requerido.").email("El email no es válido."),
  password: z
    .string()
    .min(MIN_PASSWORD_LENGTH, `La contraseña debe tener al menos ${MIN_PASSWORD_LENGTH} caracteres.`),
  fullName: z.string().trim().min(1, "El nombre completo es requerido.")
});

type CreateUserFormValues = z.input<typeof createUserSchema>;
type CreateUserValues = z.output<typeof createUserSchema>;

const createUserDefaults: CreateUserFormValues = {
  email: "",
  password: "",
  fullName: ""
};

const resetPasswordSchema = z.object({
  newPassword: z
    .string()
    .min(MIN_PASSWORD_LENGTH, `La contraseña debe tener al menos ${MIN_PASSWORD_LENGTH} caracteres.`)
});

type ResetPasswordFormValues = z.input<typeof resetPasswordSchema>;
type ResetPasswordValues = z.output<typeof resetPasswordSchema>;

const resetPasswordDefaults: ResetPasswordFormValues = {
  newPassword: ""
};

export function UserRegistryPanel({ initialUsers, roles, user }: UserRegistryPanelProps) {
  const router = useRouter();
  const [users, setUsers] = useState<UserSummary[]>(initialUsers);
  const [formOpen, setFormOpen] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<UserSummary | null>(null);
  const [deactivateDialogOpen, setDeactivateDialogOpen] = useState(false);
  const [deactivating, setDeactivating] = useState(false);
  const [deactivateError, setDeactivateError] = useState<string | null>(null);
  const [resetTarget, setResetTarget] = useState<UserSummary | null>(null);
  const [resetError, setResetError] = useState<string | null>(null);
  const [manageTargetId, setManageTargetId] = useState<string | null>(null);
  const [manageError, setManageError] = useState<string | null>(null);
  const [managing, setManaging] = useState(false);
  const [selectedRoleCode, setSelectedRoleCode] = useState("");
  const [selectedCueOption, setSelectedCueOption] = useState("");
  const [cueInput, setCueInput] = useState("");

  const canCreate = canOfferUserCreate(user);

  const {
    register: registerCreate,
    handleSubmit: handleCreateSubmit,
    reset: resetCreateForm,
    formState: { errors: createErrors, isSubmitting: creating }
  } = useForm<CreateUserFormValues, unknown, CreateUserValues>({
    resolver: zodResolver(createUserSchema),
    defaultValues: createUserDefaults
  });

  const {
    register: registerReset,
    handleSubmit: handleResetSubmit,
    reset: resetPasswordForm,
    formState: { errors: resetErrors, isSubmitting: resetting }
  } = useForm<ResetPasswordFormValues, unknown, ResetPasswordValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: resetPasswordDefaults
  });

  async function refreshUsers() {
    try {
      const updated = await callCentral<UserSummary[]>("admin/users");
      setUsers(updated);
    } catch {
      router.refresh();
    }
  }

  async function onCreateSubmit(values: CreateUserValues) {
    setCreateError(null);
    setNotice(null);
    try {
      await callCentral<UserSummary>("admin/users", {
        method: "POST",
        body: JSON.stringify(buildCreateUserPayload(values))
      });
      resetCreateForm(createUserDefaults);
      setFormOpen(false);
      await refreshUsers();
      router.refresh();
      setNotice("Usuario creado.");
    } catch (error) {
      setCreateError(getErrorMessage(error, "No se pudo crear el usuario."));
    }
  }

  function startDeactivate(target: UserSummary) {
    setDeactivateTarget(target);
    setDeactivateDialogOpen(true);
    setDeactivateError(null);
    setNotice(null);
  }

  async function confirmDeactivate() {
    if (!deactivateTarget) {
      return;
    }
    setDeactivating(true);
    setDeactivateError(null);
    try {
      await callCentral<undefined>(
        `admin/users/${encodeURIComponent(deactivateTarget.id)}/deactivate`,
        { method: "POST" }
      );
      setDeactivateDialogOpen(false);
      setDeactivateTarget(null);
      await refreshUsers();
      router.refresh();
    } catch (error) {
      setDeactivateError(getErrorMessage(error, "No se pudo desactivar el usuario."));
    } finally {
      setDeactivating(false);
    }
  }

  function startReset(target: UserSummary) {
    setResetTarget(target);
    setResetError(null);
    setNotice(null);
  }

  function cancelReset() {
    setResetTarget(null);
    resetPasswordForm(resetPasswordDefaults);
    setResetError(null);
  }

  async function onResetSubmit(values: ResetPasswordValues) {
    if (!resetTarget) {
      return;
    }
    setResetError(null);
    try {
      await callCentral<undefined>(
        `admin/users/${encodeURIComponent(resetTarget.id)}/reset-password`,
        { method: "POST", body: JSON.stringify({ newPassword: values.newPassword }) }
      );
      cancelReset();
      await refreshUsers();
      router.refresh();
      setNotice("Contraseña restablecida.");
    } catch (error) {
      setResetError(getErrorMessage(error, "No se pudo restablecer la contraseña."));
    }
  }

  function toggleManage(target: UserSummary) {
    if (manageTargetId === target.id) {
      setManageTargetId(null);
      setManageError(null);
      return;
    }
    setManageTargetId(target.id);
    setManageError(null);
    setSelectedRoleCode("");
    setSelectedCueOption("");
    setCueInput("");
  }

  async function runAssignment(request: () => Promise<undefined>, fallbackMessage: string) {
    setManaging(true);
    setManageError(null);
    try {
      await request();
      await refreshUsers();
      router.refresh();
    } catch (error) {
      setManageError(getErrorMessage(error, fallbackMessage));
    } finally {
      setManaging(false);
    }
  }

  function revokeRole(target: UserSummary, roleCode: string) {
    return runAssignment(
      () =>
        callCentral<undefined>(
          `admin/users/${encodeURIComponent(target.id)}/roles/${encodeURIComponent(roleCode)}`,
          { method: "DELETE" }
        ),
      "No se pudo quitar el rol."
    );
  }

  function assignRole(target: UserSummary, roleCode: string) {
    return runAssignment(
      () =>
        callCentral<undefined>(`admin/users/${encodeURIComponent(target.id)}/roles`, {
          method: "POST",
          body: JSON.stringify({ roleCode })
        }),
      "No se pudo asignar el rol."
    );
  }

  function revokeCue(target: UserSummary, cue: string) {
    return runAssignment(
      () =>
        callCentral<undefined>(
          `admin/users/${encodeURIComponent(target.id)}/schools/${encodeURIComponent(cue)}`,
          { method: "DELETE" }
        ),
      "No se pudo quitar el CUE."
    );
  }

  function assignCue(target: UserSummary, cue: string) {
    return runAssignment(
      () =>
        callCentral<undefined>(`admin/users/${encodeURIComponent(target.id)}/schools`, {
          method: "POST",
          body: JSON.stringify({ cue })
        }),
      "No se pudo asignar el CUE."
    );
  }

  return (
    <div className="stack">
      {resetTarget && (
        <div className="card">
          <form
            className="card__body stack"
            onSubmit={handleResetSubmit(onResetSubmit)}
            noValidate
          >
            <h3>Restablecer contraseña de {resetTarget.email}</h3>
            <p className="field__hint">
              La nueva contraseña debe tener al menos {MIN_PASSWORD_LENGTH} caracteres.
            </p>
            <TextField
              type="password"
              label="Nueva contraseña"
              required
              error={resetErrors.newPassword?.message}
              {...registerReset("newPassword")}
            />
            <div className="row">
              <Button type="submit" disabled={resetting}>
                {resetting ? "Restableciendo…" : "Restablecer contraseña"}
              </Button>
              <Button variant="secondary" onClick={cancelReset}>
                Cancelar
              </Button>
            </div>
            {resetError && <Banner tone="error">{resetError}</Banner>}
          </form>
        </div>
      )}

      <div className="card">
        <div className="card__header">
          <h2>Usuarios</h2>
          {canCreate && (
            <Button onClick={() => setFormOpen(open => !open)}>
              {formOpen ? "Cerrar" : "Nuevo usuario"}
            </Button>
          )}
        </div>
        <div className="card__body stack">
          {formOpen && canCreate && (
            <form className="stack" onSubmit={handleCreateSubmit(onCreateSubmit)} noValidate>
              <TextField
                type="email"
                label="Email"
                placeholder="Ej. ana@escuela.edu"
                required
                error={createErrors.email?.message}
                {...registerCreate("email")}
              />
              <TextField
                type="password"
                label="Contraseña"
                hint={`Mínimo ${MIN_PASSWORD_LENGTH} caracteres.`}
                required
                error={createErrors.password?.message}
                {...registerCreate("password")}
              />
              <TextField
                label="Nombre completo"
                placeholder="Ej. Ana Pérez"
                required
                error={createErrors.fullName?.message}
                {...registerCreate("fullName")}
              />
              <div className="row">
                <Button type="submit" disabled={creating}>
                  {creating ? "Creando…" : "Crear usuario"}
                </Button>
              </div>
            </form>
          )}

          {createError && <Banner tone="error">{createError}</Banner>}
          {notice && <Banner tone="success">{notice}</Banner>}
          {deactivateError && <Banner tone="error">{deactivateError}</Banner>}

          {users.length === 0 ? (
            <EmptyState
              title="Aún no hay usuarios"
              description={usersEmptyStateDescription(user)}
            />
          ) : (
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr
                  style={{
                    textAlign: "left",
                    color: "var(--text-muted)",
                    fontSize: "12px",
                    textTransform: "uppercase",
                    letterSpacing: "0.05em"
                  }}
                >
                  <th style={{ padding: "0 0 var(--space-2)" }}>Email</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Nombre completo</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Estado</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>CUEs</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Roles</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {users.map(target => {
                  const canDeactivate = canOfferUserDeactivate(user, target.cues, target.status);
                  const canReset = canOfferResetPassword(user, target.cues);
                  const canManagePanel = hasAnyRoleOrCueAction(user, target, roles);
                  const manageOpen = manageTargetId === target.id;
                  return (
                    <Fragment key={target.id}>
                      <tr style={{ borderTop: "1px solid var(--line)" }}>
                        <td style={{ padding: "var(--space-3) var(--space-1) var(--space-3) 0" }}>
                          {target.email}
                        </td>
                        <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                          {target.fullName}
                        </td>
                        <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                          <StatusBadge status={target.status} />
                        </td>
                        <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                          {formatCues(target.cues)}
                        </td>
                        <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                          {formatRoleCodes(target.roleCodes)}
                        </td>
                        <td style={{ padding: "var(--space-3) 0 var(--space-3) var(--space-1)" }}>
                          {canDeactivate || canReset || canManagePanel || manageOpen ? (
                            <div className="row">
                              {canDeactivate && (
                                <Button
                                  variant="danger"
                                  size="sm"
                                  onClick={() => startDeactivate(target)}
                                >
                                  Desactivar
                                </Button>
                              )}
                              {canReset && (
                                <Button
                                  variant="secondary"
                                  size="sm"
                                  onClick={() => startReset(target)}
                                >
                                  Restablecer contraseña
                                </Button>
                              )}
                            {(canManagePanel || manageOpen) && (
                              <Button
                                variant="secondary"
                                size="sm"
                                aria-expanded={manageOpen}
                                onClick={() => toggleManage(target)}
                              >
                                {manageOpen ? "Cerrar" : "Roles y escuelas"}
                              </Button>
                            )}
                            </div>
                          ) : (
                            <span className="field__hint">—</span>
                          )}
                        </td>
                      </tr>
                      {manageOpen && (
                        <tr>
                          <td colSpan={6} style={{ padding: "0 0 var(--space-4)" }}>
                            <div className="card">
                              <div className="card__body stack">
                                <h3>Roles y escuelas de {target.email}</h3>

                                <div className="stack">
                                  <span className="field-label">Roles asignados</span>
                                  {target.roleCodes.length === 0 ? (
                                    <p className="field__hint">Sin roles asignados.</p>
                                  ) : (
                                    <div className="row">
                                      {target.roleCodes.map(code => (
                                        <span key={code} className="badge">
                                          {code}
                                          {canOfferRoleRevoke(user, target.cues) && (
                                            <Button
                                              variant="ghost"
                                              size="sm"
                                              disabled={managing}
                                              onClick={() => void revokeRole(target, code)}
                                            >
                                              Quitar
                                            </Button>
                                          )}
                                        </span>
                                      ))}
                                    </div>
                                  )}
                                </div>

                                {(() => {
                                  const assignable = rolesAvailableToAssign(
                                    user,
                                    target.cues,
                                    target.roleCodes,
                                    roles
                                  );
                                  if (assignable.length === 0) {
                                    return null;
                                  }
                                  return (
                                    <div className="row" style={{ alignItems: "center" }}>
                                      <label htmlFor={`assign-role-${target.id}`} className="field-label">
                                        Asignar rol
                                      </label>
                                      <select
                                        id={`assign-role-${target.id}`}
                                        style={{ width: "auto" }}
                                        value={selectedRoleCode}
                                        onChange={event => setSelectedRoleCode(event.target.value)}
                                      >
                                        <option value="">Seleccioná un rol</option>
                                        {assignable.map(code => (
                                          <option key={code} value={code}>
                                            {roles.find(role => role.code === code)?.name ?? code}
                                          </option>
                                        ))}
                                      </select>
                                      <Button
                                        disabled={managing || selectedRoleCode === ""}
                                        onClick={() => void assignRole(target, selectedRoleCode)}
                                      >
                                        Asignar rol
                                      </Button>
                                    </div>
                                  );
                                })()}

                                <div className="stack">
                                  <span className="field-label">CUEs asignados</span>
                                  {target.cues.length === 0 ? (
                                    <p className="field__hint">Sin CUEs asignados.</p>
                                  ) : (
                                    <div className="row">
                                      {target.cues.map(cue => (
                                        <span key={cue} className="badge">
                                          {cue}
                                          {canOfferCueRevoke(user, cue) && (
                                            <Button
                                              variant="ghost"
                                              size="sm"
                                              disabled={managing}
                                              onClick={() => void revokeCue(target, cue)}
                                            >
                                              Quitar
                                            </Button>
                                          )}
                                        </span>
                                      ))}
                                    </div>
                                  )}
                                </div>

                                {(() => {
                                  const cueOptions = cuesAvailableToAssign(user, target.cues);
                                  if (cueOptions !== null && cueOptions.length === 0) {
                                    return null;
                                  }
                                  const typedCue = normalizeCue(cueInput);
                                  if (cueOptions === null) {
                                    return (
                                      <div className="row" style={{ alignItems: "center" }}>
                                        <TextField
                                          label="CUE"
                                          placeholder="Ej. 180000100"
                                          value={cueInput}
                                          onChange={event => setCueInput(event.target.value)}
                                          hint={
                                            cueInput !== "" && typedCue === null
                                              ? "El CUE debe tener exactamente 9 dígitos."
                                              : undefined
                                          }
                                        />
                                        <Button
                                          disabled={managing || typedCue === null}
                                          onClick={() => void assignCue(target, cueInput)}
                                        >
                                          Asignar CUE
                                        </Button>
                                      </div>
                                    );
                                  }
                                  return (
                                    <div className="row" style={{ alignItems: "center" }}>
                                      <label htmlFor={`assign-cue-${target.id}`} className="field-label">
                                        Asignar CUE
                                      </label>
                                      <select
                                        id={`assign-cue-${target.id}`}
                                        style={{ width: "auto" }}
                                        value={selectedCueOption}
                                        onChange={event => setSelectedCueOption(event.target.value)}
                                      >
                                        <option value="">Seleccioná un CUE</option>
                                        {cueOptions.map(cue => (
                                          <option key={cue} value={cue}>
                                            {cue}
                                          </option>
                                        ))}
                                      </select>
                                      <Button
                                        disabled={managing || selectedCueOption === ""}
                                        onClick={() => void assignCue(target, selectedCueOption)}
                                      >
                                        Asignar CUE
                                      </Button>
                                    </div>
                                  );
                                })()}

                                {manageError && <Banner tone="error">{manageError}</Banner>}
                              </div>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={deactivateDialogOpen}
        title={deactivateTarget ? `Desactivar usuario ${deactivateTarget.email}` : "Desactivar usuario"}
        description="El usuario dejará de poder iniciar sesión. ¿Querés continuar?"
        confirmLabel="Sí, desactivar"
        cancelLabel="Cancelar"
        busy={deactivating}
        onConfirm={() => void confirmDeactivate()}
        onCancel={() => setDeactivateDialogOpen(false)}
      />
    </div>
  );
}
