import type { GlossaryTerm } from "./glossary";

const ROLE_LABELS: Record<string, string> = {
  Admin: "Administrador",
  RosterProvince: "Padrón provincial",
  RosterSchool: "Padrón escolar",
  ExamAuthor: "Autor de exámenes",
  Grader: "Corrector",
  Operator: "Operador"
};

/** Spanish label for a role code. Unknown codes are returned unchanged. */
export function roleLabel(code: string): string {
  return ROLE_LABELS[code] ?? code;
}

const ROLE_TERMS: Record<string, GlossaryTerm> = {
  Admin: "rol-administrador",
  RosterProvince: "rol-padron-provincial",
  RosterSchool: "rol-padron-escolar",
  ExamAuthor: "rol-autor-examenes",
  Grader: "rol-corrector",
  Operator: "rol-operador"
};

/** Glosario term that explains a role code, or null when the code is unknown. */
export function roleTerm(code: string): GlossaryTerm | null {
  return ROLE_TERMS[code] ?? null;
}
