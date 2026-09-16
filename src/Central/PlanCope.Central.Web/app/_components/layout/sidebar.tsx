"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/dashboard", label: "Inicio" },
  { href: "/exams", label: "Exámenes" },
  { href: "/claves", label: "Claves de activación" },
  { href: "/nodos", label: "Nodos" },
  { href: "/estadisticas", label: "Estadísticas" },
  { href: "/politicas-legado", label: "Políticas de puntaje heredadas" },
  { href: "/descargas", label: "Descargas" }
];

export function Sidebar() {
  const pathname = usePathname();

  return (
    <aside className="sidebar">
      <div className="sidebar__brand">
        <span className="sidebar__mark" aria-hidden="true">
          PC
        </span>
        <strong>PlanCope Central</strong>
      </div>
      <nav className="sidebar__nav" aria-label="Navegación principal">
        {links.map(link => {
          const active = pathname === link.href || pathname.startsWith(`${link.href}/`);
          return (
            <Link
              key={link.href}
              href={link.href}
              className="sidebar__link"
              aria-current={active ? "page" : undefined}
            >
              {link.label}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
