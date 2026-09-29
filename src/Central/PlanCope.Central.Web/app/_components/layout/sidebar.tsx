"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  BarChart3,
  Download,
  FileText,
  Home,
  KeyRound,
  School,
  ScrollText,
  Server,
  Users,
  type LucideIcon
} from "lucide-react";
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
  { href: "/nodos", label: "Nodos", icon: Server },
  { href: "/estadisticas", label: "Estadísticas", icon: BarChart3 },
  { href: "/politicas-legado", label: "Políticas de puntaje heredadas", icon: ScrollText },
  { href: "/descargas", label: "Descargas", icon: Download }
];

export function AppSidebar() {
  const pathname = usePathname();

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="p-3">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" tooltip="PlanCope Central" render={<Link href="/dashboard" />}>
              <span className="grid size-8 shrink-0 place-items-center rounded-md bg-primary text-sm font-bold text-primary-foreground">
                PC
              </span>
              <span className="grid flex-1 gap-0 text-left">
                <span className="truncate text-sm leading-tight font-semibold">PlanCope Central</span>
                <span className="truncate text-xs leading-tight text-muted-foreground">Administración</span>
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
              {links.map(link => {
                const active = pathname === link.href || pathname.startsWith(`${link.href}/`);
                const Icon = link.icon;
                return (
                  <SidebarMenuItem key={link.href}>
                    <SidebarMenuButton
                      isActive={active}
                      tooltip={link.label}
                      render={<Link href={link.href} />}
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
