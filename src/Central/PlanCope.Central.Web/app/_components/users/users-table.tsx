"use client";

import { useState } from "react";
import { MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from "@/components/ui/table";
import { DeactivateUserDialog } from "./deactivate-user-dialog";
import { ManageUserDialog } from "./manage-user-dialog";
import { ResetPasswordDialog } from "./reset-password-dialog";
import {
  canOfferUserDeactivate,
  canOfferResetPassword,
  formatCues,
  hasAnyRoleOrCueAction,
  roleLabel,
  userStatusLabel,
  usersEmptyStateDescription
} from "./user-mapping";
import type { RoleSummary, UserSummary } from "../../_lib/api/server";
import type { UserProfile } from "../../_lib/contracts";

interface UsersTableProps {
  users: UserSummary[];
  roles: RoleSummary[];
  user: UserProfile;
}

function RoleBadges({ codes }: { codes: string[] }) {
  if (codes.length === 0) {
    return <span className="text-muted-foreground">—</span>;
  }
  return (
    <div className="flex flex-wrap gap-1">
      {codes.map(code => (
        <Badge key={code} variant="secondary">
          {roleLabel(code)}
        </Badge>
      ))}
    </div>
  );
}

export function UsersTable({ users, roles, user }: UsersTableProps) {
  const [resetId, setResetId] = useState<string | null>(null);
  const [manageId, setManageId] = useState<string | null>(null);
  const [deactivateId, setDeactivateId] = useState<string | null>(null);

  const resetTarget = users.find(candidate => candidate.id === resetId) ?? null;
  const manageTarget = users.find(candidate => candidate.id === manageId) ?? null;
  const deactivateTarget = users.find(candidate => candidate.id === deactivateId) ?? null;

  if (users.length === 0) {
    return (
      <p className="rounded-xl border py-10 text-center text-sm text-muted-foreground">
        {usersEmptyStateDescription(user)}
      </p>
    );
  }

  return (
    <>
      <div className="overflow-hidden rounded-xl border">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow>
              <TableHead>Nombre</TableHead>
              <TableHead>Correo</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Roles</TableHead>
              <TableHead>Escuelas (CUEs)</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.map(target => {
              const canDeactivate = canOfferUserDeactivate(user, target.cues, target.status);
              const canReset = canOfferResetPassword(user, target.cues);
              const canManage = hasAnyRoleOrCueAction(user, target, roles);
              const hasActions = canDeactivate || canReset || canManage;
              return (
                <TableRow key={target.id}>
                  <TableCell className="font-medium">{target.fullName}</TableCell>
                  <TableCell>{target.email}</TableCell>
                  <TableCell>
                    <Badge variant={target.status.toLowerCase() === "active" ? "default" : "secondary"}>
                      {userStatusLabel(target.status)}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <RoleBadges codes={target.roleCodes} />
                  </TableCell>
                  <TableCell className="text-muted-foreground">{formatCues(target.cues)}</TableCell>
                  <TableCell className="text-right">
                    {hasActions ? (
                      <DropdownMenu>
                        <DropdownMenuTrigger
                          render={<Button variant="ghost" size="icon-sm" aria-label="Acciones" />}
                        >
                          <MoreHorizontal />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {canReset && (
                            <DropdownMenuItem onClick={() => setResetId(target.id)}>
                              Restablecer contraseña
                            </DropdownMenuItem>
                          )}
                          {canManage && (
                            <DropdownMenuItem onClick={() => setManageId(target.id)}>
                              Roles y escuelas
                            </DropdownMenuItem>
                          )}
                          {canDeactivate && (
                            <DropdownMenuItem
                              variant="destructive"
                              onClick={() => setDeactivateId(target.id)}
                            >
                              Desactivar
                            </DropdownMenuItem>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      {resetTarget && <ResetPasswordDialog target={resetTarget} onClose={() => setResetId(null)} />}
      {manageTarget && (
        <ManageUserDialog
          target={manageTarget}
          roles={roles}
          user={user}
          onClose={() => setManageId(null)}
        />
      )}
      {deactivateTarget && (
        <DeactivateUserDialog target={deactivateTarget} onClose={() => setDeactivateId(null)} />
      )}
    </>
  );
}
