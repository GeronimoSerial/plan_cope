export const CUE_LENGTH = 9;

export function normalizeCueInput(value: string): string {
  return value.replace(/\D/g, "").slice(0, CUE_LENGTH);
}

export function isValidCue(value: string): boolean {
  return /^\d{9}$/.test(value);
}
