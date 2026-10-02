"use client";

import { useState } from "react";
import Link from "next/link";
import Image from "next/image";
import { usePathname, useRouter } from "next/navigation";
import { LogOut } from "lucide-react";
import { useNavigationGuard } from "./navigation-guard";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList
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
  estadisticas: "Estadísticas",
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

function ancestorFor(pathname: string) {
  return pathname.startsWith("/exams/") ? { label: sectionLabels.exams, href: "/exams" } : null;
}

export function AppHeader({ user }: AppHeaderProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { intercept } = useNavigationGuard();
  const [loading, setLoading] = useState(false);
  const ancestor = ancestorFor(pathname);

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
    <header className="central-header">
      <div className="central-ribbon" aria-hidden="true"><span /><span /><span /><span /><span /></div>
      <div className="central-signature">
        <div className="central-signature__inner">
          <Link href="/dashboard" aria-label="PlanCope Central, inicio">
            <Image className="central-logo" src="/marca/logo-educacion-h.svg" width={300} height={60} priority alt="Gobierno de Corrientes - Ministerio de Educación" />
          </Link>
          <div className="central-institutions" role="group" aria-label="Direcciones institucionales">
            <span className="central-reparticiones">Dirección de Planeamiento e Investigación Educativa</span>
            <span className="central-reparticiones">Dirección de Sistemas de Información</span>
          </div>
          <span className="central-province">Provincia de Corrientes<br />República Argentina</span>
        </div>
      </div>
      <div className="central-band">
        <div className="central-band__inner">
          <SidebarTrigger aria-label="Alternar navegación" />
          <Link className="central-brand" href="/dashboard">Plan COPE · Central</Link>
          {ancestor ? (
            <div className="min-w-0 flex-1 overflow-hidden">
              <Breadcrumb>
                <BreadcrumbList className="flex-nowrap">
                  <BreadcrumbItem>
                    <BreadcrumbLink
                      render={<Link href={ancestor.href} />}
                      onClick={event => {
                        event.preventDefault();
                        if (!intercept(() => router.push(ancestor.href))) router.push(ancestor.href);
                      }}
                    >
                      {ancestor.label}
                    </BreadcrumbLink>
                  </BreadcrumbItem>
                </BreadcrumbList>
              </Breadcrumb>
            </div>
          ) : <div className="flex-1" aria-hidden="true" />}
          <DropdownMenu>
            <DropdownMenuTrigger
              render={<Button variant="ghost" size="icon" aria-label="Menú de usuario" />}
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
        </div>
      </div>
    </header>
  );
}
