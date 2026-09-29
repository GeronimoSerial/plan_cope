"use client";

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function ExamsError({ reset }: { error: Error; reset: () => void }) {
  return (
    <Alert variant="destructive">
      <AlertTitle>No se pudieron cargar los exámenes</AlertTitle>
      <AlertDescription>
        <p className="mb-3">Volvé a intentarlo en unos segundos.</p>
        <Button variant="outline" size="sm" onClick={reset}>
          Reintentar
        </Button>
      </AlertDescription>
    </Alert>
  );
}
