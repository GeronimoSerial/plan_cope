import { redirect } from "next/navigation";
import { getSessionUser } from "../_lib/server/session";
import { AppSidebar } from "../_components/layout/sidebar";
import { AppHeader } from "../_components/layout/app-header";
import { SidebarProvider } from "@/components/ui/sidebar";
import { TooltipProvider } from "@/components/ui/tooltip";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSessionUser();
  if (!user) {
    redirect("/login");
  }

  return (
    <TooltipProvider>
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      <SidebarProvider>
        <AppSidebar />
        <div className="flex min-w-0 flex-1 flex-col">
          <AppHeader user={user} />
          <main id="contenido" className="flex-1 p-4 md:p-6">
            {children}
          </main>
        </div>
      </SidebarProvider>
    </TooltipProvider>
  );
}
