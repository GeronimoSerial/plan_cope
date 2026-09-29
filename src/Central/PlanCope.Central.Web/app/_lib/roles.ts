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
