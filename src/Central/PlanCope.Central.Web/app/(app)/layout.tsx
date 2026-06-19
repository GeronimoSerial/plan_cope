import { redirect } from "next/navigation";
import { getSessionUser } from "../_lib/server/session";
import { Sidebar } from "../_components/layout/sidebar";
import { AppHeader } from "../_components/layout/app-header";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSessionUser();
  if (!user) {
    redirect("/login");
  }

  return (
    <>
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      <div className="app-shell">
        <Sidebar />
        <div className="app-main">
          <AppHeader user={user} />
          <main id="contenido" className="app-content">
            {children}
          </main>
        </div>
      </div>
    </>
  );
}
