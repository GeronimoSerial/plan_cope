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
import { UserRegistryPanel } from "../../_components/users/user-registry-panel";

export const metadata: Metadata = { title: "Usuarios · PlanCope Central" };

export default async function UsersPage() {
  const user = await getSessionUser();

  let users: UserSummary[];
  let roles: RoleSummary[];
  try {
    users = await listUsers();
    roles = await listRoles();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Usuarios"
        title="Usuarios"
        description="Administrá los usuarios de la instalación y su acceso."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Usuarios" }]}
      />

      <UserRegistryPanel initialUsers={users} roles={roles} user={user!} />
    </>
  );
}
