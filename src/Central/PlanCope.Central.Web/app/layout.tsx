import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PlanCope Central",
  description: "Administración central y builder online de exámenes"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="es">
      <body>{children}</body>
    </html>
  );
}
