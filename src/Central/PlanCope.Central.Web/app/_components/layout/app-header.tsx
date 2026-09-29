"use client";

import { useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { LogOut } from "lucide-react";
import { useNavigationGuard } from "./navigation-guard";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage
} from "@/components/ui/breadcrumb";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
import { SidebarTrigger } from "@/components/ui/sidebar";
import { roleLabel } from "../../_lib/roles";
import type { UserProfile } from "../../_lib/contracts";

interface AppHeaderProps {
  user: Pick<UserProfile, "displayName" | "role">;
}

const sectionLabels: Record<string, string> = {
  dashboard: "Inicio",
  exams: "Exámenes",
  escuelas: "Escuelas",
  usuarios: "Usuarios",
  claves: "Claves de activación",
  nodos: "Nodos",
  estadisticas: "Estadísticas",
  "politicas-legado": "Reglas de puntaje pendientes",
  descargas: "Descargas"
};

function initials(name: string) {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase())
    .join("");
}

function sectionFor(pathname: string) {
  const segment = pathname.split("/").filter(Boolean)[0] ?? "dashboard";
  return sectionLabels[segment] ?? "Inicio";
}

export function AppHeader({ user }: AppHeaderProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { intercept } = useNavigationGuard();
  const [loading, setLoading] = useState(false);

  async function logout() {
    setLoading(true);
    try {
      await fetch("/api/session/logout", { method: "POST" });
    } finally {
      router.replace("/login");
      router.refresh();
    }
  }

  // Logging out leaves the current view; respect the builder's unsaved-changes guard first.
  function requestLogout() {
    if (!intercept(() => void logout())) {
      void logout();
    }
  }

  return (
    <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-2 border-b bg-background px-4 md:px-6">
      <SidebarTrigger className="-ml-1" />
      <div aria-hidden="true" className="mx-1 h-5 w-px shrink-0 bg-border" />
      <div className="min-w-0 flex-1 overflow-hidden">
        <Breadcrumb>
          <BreadcrumbList className="flex-nowrap">
            <BreadcrumbItem>
              <BreadcrumbPage className="block truncate">{sectionFor(pathname)}</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </div>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={<Button variant="ghost" size="icon" className="rounded-full" aria-label="Menú de usuario" />}
        >
          <Avatar size="sm">
            <AvatarFallback>{initials(user.displayName) || "U"}</AvatarFallback>
          </Avatar>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          <div className="flex min-w-0 flex-col gap-0.5 px-1.5 py-1.5">
            <span className="truncate text-sm font-medium">{user.displayName}</span>
            <span className="truncate text-xs text-muted-foreground">{roleLabel(user.role)}</span>
          </div>
          <DropdownMenuSeparator />
          <DropdownMenuItem variant="destructive" disabled={loading} onClick={requestLogout}>
            <LogOut />
            {loading ? "Saliendo…" : "Cerrar sesión"}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}
