export function safeInternalPath(value: string | null | undefined, origin: string): string {
  if (typeof value !== "string" || !value) return "/dashboard";
  const hasUnsafeCharacters = (input: string) => /[\\\\\t\r\n]/.test(input);
  if (hasUnsafeCharacters(value)) return "/dashboard";
  try {
    const decoded = decodeURIComponent(value);
    if (hasUnsafeCharacters(decoded)) return "/dashboard";
  } catch {
    return "/dashboard";
  }
  try {
    const base = new URL(origin).origin;
    const parsed = new URL(value, base);
    return parsed.origin === base ? `${parsed.pathname}${parsed.search}` : "/dashboard";
  } catch {
    return "/dashboard";
  }
}
