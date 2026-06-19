import type { ReactNode } from "react";

type Tone = "info" | "success" | "error";

interface BannerProps {
  tone?: Tone;
  children: ReactNode;
}

export function Banner({ tone = "info", children }: BannerProps) {
  // role=alert para errores (lectores de pantalla lo anuncian), status para el resto.
  const role = tone === "error" ? "alert" : "status";
  return (
    <div className={`banner banner--${tone}`} role={role}>
      {children}
    </div>
  );
}
