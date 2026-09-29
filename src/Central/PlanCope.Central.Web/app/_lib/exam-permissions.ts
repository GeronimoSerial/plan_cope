export const EXAM_EDIT_PERMISSION_MESSAGE = "No tenés permiso para editar exámenes. Pedile a un administrador el rol Autor de exámenes.";

export function canEditExams(roles: string | string[] | null | undefined): boolean {
  const normalized = Array.isArray(roles) ? roles : typeof roles === "string" ? roles.split(/[;,]/) : [];
  return normalized.some(role => ["Admin", "ExamAuthor"].includes(role.trim()));
}

export function examWriteErrorMessage(status: number, fallback: string): string {
  return status === 403 ? EXAM_EDIT_PERMISSION_MESSAGE : fallback;
}
