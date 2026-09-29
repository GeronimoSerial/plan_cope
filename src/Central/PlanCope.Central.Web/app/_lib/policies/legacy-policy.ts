// Pure helpers for the one-off legacy grading-policy migration tool.
// Kept free of React so they can be unit-tested in isolation.

export interface SelectableVersion {
  examVersionId: string;
}

export function formatPublishedDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "—";
  }
  return date.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" });
}

export function toggleOne(selected: readonly string[], examVersionId: string, checked: boolean): string[] {
  if (checked) {
    return selected.includes(examVersionId) ? [...selected] : [...selected, examVersionId];
  }
  return selected.filter(id => id !== examVersionId);
}

export function toggleAll(allIds: readonly string[], checked: boolean): string[] {
  return checked ? [...allIds] : [];
}

export function isAllSelected(selectedCount: number, totalCount: number): boolean {
  return totalCount > 0 && selectedCount === totalCount;
}

export function isSomeSelected(selectedCount: number, totalCount: number): boolean {
  return selectedCount > 0 && selectedCount < totalCount;
}

export function assignButtonLabel(count: number): string {
  if (count === 0) {
    return "Asignar";
  }
  return count === 1 ? "Asignar a 1 versión" : `Asignar a ${count} versiones`;
}

export function assignedToastMessage(count: number): string {
  return count === 1 ? "Se asignó la regla a 1 versión" : `Se asignó la regla a ${count} versiones`;
}

export function rejectedAlertMessage(count: number): string {
  return count === 1 ? "1 versión fue rechazada" : `${count} versiones fueron rechazadas`;
}

export function selectionHint(count: number): string {
  if (count === 0) {
    return "Seleccioná al menos una versión.";
  }
  return count === 1 ? "1 versión seleccionada." : `${count} versiones seleccionadas.`;
}

export function confirmDescription(policyLabel: string, count: number): string {
  const versions = count === 1 ? "1 versión" : `${count} versiones`;
  return `¿Asignar la regla "${policyLabel}" a ${versions}? Se aplica a exámenes ya publicados.`;
}

// After a bulk assignment, drop the versions that were actually assigned and keep
// the rejected ones so the operator can retry them.
export function applyAssignmentResult<T extends SelectableVersion>(
  versions: readonly T[],
  selectedIds: readonly string[],
  rejectedIds: readonly string[]
): T[] {
  const rejected = new Set(rejectedIds);
  const selected = new Set(selectedIds);
  return versions.filter(version => !selected.has(version.examVersionId) || rejected.has(version.examVersionId));
}
