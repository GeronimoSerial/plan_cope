import type { Metadata } from "next";
import { Barlow, Barlow_Semi_Condensed, Montserrat } from "next/font/google";
import "./globals.css";
import { cn } from "@/lib/utils";
import { Toaster } from "@/components/ui/sonner";

const barlow = Barlow({ subsets: ["latin"], variable: "--font-barlow", display: "swap", weight: ["400", "500", "600", "700"] });
const barlowSemiCondensed = Barlow_Semi_Condensed({ subsets: ["latin"], variable: "--font-barlow-condensed", display: "swap", weight: ["700", "800"] });
const montserrat = Montserrat({ subsets: ["latin"], variable: "--font-montserrat", display: "swap", weight: ["700"] });

export const metadata: Metadata = {
  title: "PlanCope Central",
  description: "Administración central y builder online de exámenes",
  icons: { icon: "/marca/escudo.svg", apple: "/marca/escudo.png" }
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="es" className={cn("font-sans", barlow.variable, barlowSemiCondensed.variable, montserrat.variable)}>
      <body>
        {children}
        <Toaster position="top-right" />
      </body>
    </html>
  );
}
