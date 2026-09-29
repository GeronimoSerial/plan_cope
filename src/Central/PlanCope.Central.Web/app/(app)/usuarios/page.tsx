import { redirect } from "next/navigation";
import type { Metadata } from "next";
import {
  isSessionExpired,
  listRoles,
  listUsers,
  type RoleSummary,
  type UserSummary
} from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { CreateUserButton } from "../../_components/users/create-user-dialog";
import { UsersTable } from "../../_components/users/users-table";

export const metadata: Metadata = { title: "Usuarios · PlanCope Central" };

export default async function UsersPage() {
  const user = await getSessionUser();

  let users: UserSummary[];
  let roles: RoleSummary[];
  try {
    [users, roles] = await Promise.all([listUsers(), listRoles()]);
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader title="Usuarios" actions={<CreateUserButton user={user!} />} />
      <UsersTable users={users} roles={roles} user={user!} />
    </>
  );
}
