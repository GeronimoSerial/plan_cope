import type { Metadata } from "next";
import "./styles.css";

export const metadata: Metadata = {
  title: "PlanCope Central",
  description: "Administracion central y builder online de examenes"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="es">
      <body>{children}</body>
    </html>
  );
}
