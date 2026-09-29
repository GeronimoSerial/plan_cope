"use client";

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function BuilderError({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <div className="grid gap-4">
      <Alert variant="destructive">
        <AlertTitle>No se pudo cargar el builder</AlertTitle>
        <AlertDescription>{error.message}</AlertDescription>
      </Alert>
      <div>
        <Button type="button" variant="outline" onClick={reset}>
          Reintentar
        </Button>
      </div>
    </div>
  );
}
