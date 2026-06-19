"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "../ui/button";
import type { UserProfile } from "../../_lib/contracts";

interface AppHeaderProps {
  user: Pick<UserProfile, "displayName" | "role">;
}

function initials(name: string) {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase())
    .join("");
}

export function AppHeader({ user }: AppHeaderProps) {
  const router = useRouter();
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

  return (
    <header className="app-header">
      <div className="app-header__user">
        <span className="app-header__avatar" aria-hidden="true">
          {initials(user.displayName) || "U"}
        </span>
        <span className="app-header__meta">
          <strong>{user.displayName}</strong>
          <small>{user.role}</small>
        </span>
      </div>
      <Button variant="secondary" size="sm" onClick={() => void logout()} disabled={loading}>
        {loading ? "Saliendo…" : "Cerrar sesión"}
      </Button>
    </header>
  );
}
