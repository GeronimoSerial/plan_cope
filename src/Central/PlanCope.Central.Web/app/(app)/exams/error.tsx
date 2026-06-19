"use client";

import { Banner } from "../../_components/ui/banner";
import { Button } from "../../_components/ui/button";

export default function ExamsError({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <div className="stack">
      <Banner tone="error">No se pudieron cargar los exámenes: {error.message}</Banner>
      <div>
        <Button variant="secondary" onClick={reset}>
          Reintentar
        </Button>
      </div>
    </div>
  );
}
