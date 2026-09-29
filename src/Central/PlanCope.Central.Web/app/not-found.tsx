import Link from "next/link";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <main className="grid min-h-svh place-items-center bg-background p-6">
      <div className="grid justify-items-center gap-3 text-center">
        <p className="text-sm font-medium text-muted-foreground">404</p>
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">Página no encontrada</h1>
        <p className="text-sm text-muted-foreground">La página que buscás no existe o ya no está disponible.</p>
        <Button nativeButton={false} render={<Link href="/dashboard" />} className="mt-2">
          Volver al inicio
        </Button>
      </div>
    </main>
  );
}
