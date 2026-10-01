"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useNavigationGuard } from "./navigation-guard";
import {
  BarChart3,
  Activity,
  Download,
  FileText,
  Home,
  KeyRound,
  RefreshCw,
  School,
  Users,
  type LucideIcon
} from "lucide-react";
import type { UserProfile } from "../../_lib/contracts";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail
} from "@/components/ui/sidebar";

interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
}

const links: NavItem[] = [
  { href: "/dashboard", label: "Inicio", icon: Home },
  { href: "/exams", label: "Exámenes", icon: FileText },
  { href: "/escuelas", label: "Escuelas", icon: School },
  { href: "/usuarios", label: "Usuarios", icon: Users },
  { href: "/claves", label: "Claves de activación", icon: KeyRound },
  { href: "/estadisticas", label: "Estadísticas", icon: BarChart3 },
  { href: "/sincronizacion-recibida", label: "Sincronización recibida", icon: RefreshCw },
  { href: "/descargas", label: "Descargas", icon: Download }
];

export function AppSidebar({ user }: { user: UserProfile }) {
  const pathname = usePathname();
  const router = useRouter();
  const { intercept } = useNavigationGuard();

  // Sidebar links must respect the builder's unsaved-changes guard: when dirty, prevent the
  // default navigation and let the shared dialog decide whether to leave.
  function guardNavigation(event: React.MouseEvent, href: string) {
    if (intercept(() => router.push(href))) {
      event.preventDefault();
    }
  }

  return (
    <Sidebar className="central-sidebar" collapsible="icon">
      <SidebarHeader className="p-3">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton
              size="lg"
              tooltip="PlanCope Central"
              render={<Link href="/dashboard" />}
              onClick={event => guardNavigation(event, "/dashboard")}
            >
              <span className="grid flex-1 gap-0 text-left">
                <span className="truncate text-sm leading-tight font-semibold">Administración</span>
              </span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Navegación</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {[...links.filter(link => link.href !== "/sincronizacion-recibida" || user.role === "Admin"),
                ...(user.role === "Admin" ? [{ href: "/sesiones-en-curso", label: "Sesiones en curso", icon: Activity }] : [])].map(link => {
                const active = pathname === link.href || pathname.startsWith(`${link.href}/`);
                const Icon = link.icon;
                return (
                  <SidebarMenuItem key={link.href}>
                    <SidebarMenuButton
                      isActive={active}
                      tooltip={link.label}
                      render={<Link href={link.href} />}
                      onClick={event => guardNavigation(event, link.href)}
                    >
                      <Icon />
                      <span>{link.label}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
