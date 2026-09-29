"use client";

import { useState, type ReactNode } from "react";
import { Info } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { glossary, type GlossaryTerm } from "../../_lib/glossary";

interface TermHintProps {
  term: GlossaryTerm;
}

// Ícono de ayuda con la definición del glosario. Se abre con hover y foco (Base UI), y en
// pantallas táctiles un tap lo abre/cierra (hover es mouse-only en Base UI, por eso el toggle).
export function TermHint({ term }: TermHintProps) {
  const [open, setOpen] = useState(false);
  const entry = glossary[term];
  return (
    <Tooltip open={open} onOpenChange={setOpen}>
      <TooltipTrigger
        closeOnClick={false}
        aria-label={`Qué es ${entry.label}`}
        onPointerDown={event => {
          if (event.pointerType === "touch" || event.pointerType === "pen") {
            setOpen(current => !current);
          }
        }}
        className="inline-flex size-4 shrink-0 cursor-help items-center justify-center rounded-full align-middle text-muted-foreground outline-none transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
      >
        <Info className="size-3.5" aria-hidden="true" />
      </TooltipTrigger>
      <TooltipContent>{entry.definition}</TooltipContent>
    </Tooltip>
  );
}

interface TermLabelProps {
  term: GlossaryTerm;
  children: ReactNode;
}

// Etiqueta con su ayuda al lado: <TermLabel term="nodo">Nodos</TermLabel>.
export function TermLabel({ term, children }: TermLabelProps) {
  return (
    <span className="inline-flex items-center gap-1.5">
      {children}
      <TermHint term={term} />
    </span>
  );
}
