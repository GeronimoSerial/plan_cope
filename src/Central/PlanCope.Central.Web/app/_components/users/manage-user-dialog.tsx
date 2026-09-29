"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { XIcon } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { normalizeCue } from "../../_lib/scope";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { FieldError } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue
} from "@/components/ui/select";
import {
  canOfferCueRevoke,
  canOfferRoleRevoke,
  cuesAvailableToAssign,
  roleLabel,
  roleTerm,
  rolesAvailableToAssign
} from "./user-mapping";
import { TermLabel } from "../help/term-hint";
import type { RoleSummary, UserSummary } from "../../_lib/api/server";
import type { UserProfile } from "../../_lib/contracts";

interface ManageUserDialogProps {
  target: UserSummary;
  roles: RoleSummary[];
  user: Pick<UserProfile, "role" | "rosterScope" | "rosterCues">;
  onClose: () => void;
}

export function ManageUserDialog({ target, roles, user, onClose }: ManageUserDialogProps) {
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [selectedRole, setSelectedRole] = useState("");
  const [selectedCue, setSelectedCue] = useState("");
  const [cueInput, setCueInput] = useState("");

  const assignableRoles = rolesAvailableToAssign(user, target.cues, target.roleCodes, roles);
  const roleLabels: Record<string, string> = {};
  for (const code of assignableRoles) {
    roleLabels[code] = roleLabel(code);
  }
  const cueOptions = cuesAvailableToAssign(user, target.cues);
  const typedCue = normalizeCue(cueInput);
  const cueInputInvalid = cueInput !== "" && typedCue === null;

  async function run(action: () => Promise<unknown>, successMessage: string, fallback: string) {
    setPending(true);
    try {
      await action();
      toast.success(successMessage);
      router.refresh();
      return true;
    } catch (error) {
      toast.error(getErrorMessage(error, fallback));
      return false;
    } finally {
      setPending(false);
    }
  }

  function revokeRole(roleCode: string) {
    return run(
      () =>
        callCentral<undefined>(
          `admin/users/${encodeURIComponent(target.id)}/roles/${encodeURIComponent(roleCode)}`,
          { method: "DELETE" }
        ),
      "Rol quitado.",
      "No se pudo quitar el rol."
    );
  }

  async function assignRole() {
    const ok = await run(
      () =>
        callCentral<undefined>(`admin/users/${encodeURIComponent(target.id)}/roles`, {
          method: "POST",
          body: JSON.stringify({ roleCode: selectedRole })
        }),
      "Rol asignado.",
      "No se pudo asignar el rol."
    );
    if (ok) {
      setSelectedRole("");
    }
  }

  function revokeCue(cue: string) {
    return run(
      () =>
        callCentral<undefined>(
          `admin/users/${encodeURIComponent(target.id)}/schools/${encodeURIComponent(cue)}`,
          { method: "DELETE" }
        ),
      "CUE quitado.",
      "No se pudo quitar el CUE."
    );
  }

  async function assignCue(cue: string) {
    const ok = await run(
      () =>
        callCentral<undefined>(`admin/users/${encodeURIComponent(target.id)}/schools`, {
          method: "POST",
          body: JSON.stringify({ cue })
        }),
      "CUE asignado.",
      "No se pudo asignar el CUE."
    );
    if (ok) {
      setSelectedCue("");
      setCueInput("");
    }
  }

  return (
    <Dialog
      open
      onOpenChange={next => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Roles y escuelas</DialogTitle>
          <DialogDescription>{target.email}</DialogDescription>
        </DialogHeader>

        <div className="grid gap-5 py-1">
          <section className="grid gap-2">
            <h3 className="text-sm font-medium">
              <TermLabel term="rol">Roles asignados</TermLabel>
            </h3>
            {target.roleCodes.length === 0 ? (
              <p className="text-sm text-muted-foreground">Sin roles asignados.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {target.roleCodes.map(code => {
                  const term = roleTerm(code);
                  const badge = <Badge variant="secondary">{roleLabel(code)}</Badge>;
                  return (
                    <span key={code} className="inline-flex items-center gap-1">
                      {term ? <TermLabel term={term}>{badge}</TermLabel> : badge}
                      {canOfferRoleRevoke(user, target.cues, code) && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-xs"
                          aria-label={`Quitar rol ${roleLabel(code)}`}
                          disabled={pending}
                          onClick={() => void revokeRole(code)}
                        >
                          <XIcon />
                        </Button>
                      )}
                    </span>
                  );
                })}
              </div>
            )}
          </section>

          {assignableRoles.length > 0 && (
            <section className="grid gap-2">
              <h3 className="text-sm font-medium">Asignar rol</h3>
              <div className="flex items-end gap-2">
                <Select
                  value={selectedRole || null}
                  onValueChange={value => setSelectedRole(value ?? "")}
                  items={roleLabels}
                >
                  <SelectTrigger className="w-full" aria-label="Rol a asignar">
                    <SelectValue placeholder="Seleccioná un rol" />
                  </SelectTrigger>
                  <SelectContent>
                    {assignableRoles.map(code => (
                      <SelectItem key={code} value={code}>
                        {roleLabels[code]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  type="button"
                  disabled={pending || selectedRole === ""}
                  onClick={() => void assignRole()}
                >
                  Asignar
                </Button>
              </div>
            </section>
          )}

          <section className="grid gap-2">
            <h3 className="text-sm font-medium">
              <TermLabel term="cue">Escuelas (CUEs) asignadas</TermLabel>
            </h3>
            {target.cues.length === 0 ? (
              <p className="text-sm text-muted-foreground">Sin CUEs asignados.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {target.cues.map(cue => (
                  <span key={cue} className="inline-flex items-center gap-1">
                    <Badge variant="secondary">{cue}</Badge>
                    {canOfferCueRevoke(user, cue) && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon-xs"
                        aria-label={`Quitar CUE ${cue}`}
                        disabled={pending}
                        onClick={() => void revokeCue(cue)}
                      >
                        <XIcon />
                      </Button>
                    )}
                  </span>
                ))}
              </div>
            )}
          </section>

          {cueOptions === null ? (
            <section className="grid gap-2">
              <h3 className="text-sm font-medium">Asignar CUE</h3>
              <div className="flex items-end gap-2">
                <div className="grid flex-1 gap-1.5">
                  <Input
                    aria-label="CUE"
                    inputMode="numeric"
                    placeholder="Ej. 180000100"
                    value={cueInput}
                    aria-invalid={cueInputInvalid ? true : undefined}
                    onChange={event => setCueInput(event.target.value)}
                  />
                  {cueInputInvalid && <FieldError>El CUE debe tener exactamente 9 dígitos.</FieldError>}
                </div>
                <Button
                  type="button"
                  disabled={pending || typedCue === null}
                  onClick={() => void assignCue(cueInput)}
                >
                  Asignar
                </Button>
              </div>
            </section>
          ) : (
            cueOptions.length > 0 && (
              <section className="grid gap-2">
                <h3 className="text-sm font-medium">Asignar CUE</h3>
                <div className="flex items-end gap-2">
                  <Select
                    value={selectedCue || null}
                    onValueChange={value => setSelectedCue(value ?? "")}
                  >
                    <SelectTrigger className="w-full" aria-label="CUE a asignar">
                      <SelectValue placeholder="Seleccioná un CUE" />
                    </SelectTrigger>
                    <SelectContent>
                      {cueOptions.map(cue => (
                        <SelectItem key={cue} value={cue}>
                          {cue}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Button
                    type="button"
                    disabled={pending || selectedCue === ""}
                    onClick={() => void assignCue(selectedCue)}
                  >
                    Asignar
                  </Button>
                </div>
              </section>
            )
          )}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            Cerrar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
