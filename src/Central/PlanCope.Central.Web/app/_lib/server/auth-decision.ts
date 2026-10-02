export type ProxyAuthDecision = { kind: "next" | "refresh" | "redirect-clear" };

export function decideProxyAuth(cookies: readonly string[]): ProxyAuthDecision {
  if (cookies.includes("pc_at")) return { kind: "next" };
  if (cookies.includes("pc_rt")) return { kind: "refresh" };
  return { kind: "redirect-clear" };
}

export function isPublicSharedStatsPath(pathname: string): boolean {
  return /^\/estadisticas\/compartidas\/[^/]+$/.test(pathname);
}

export function decideLoginAuth(input: { hasAccess: boolean; expired: boolean }): boolean {
  return input.hasAccess && !input.expired;
}

export function decideExpiredPage(refreshAlreadyAttempted: boolean): "refresh" | "login" {
  return refreshAlreadyAttempted ? "login" : "refresh";
}

export function buildForwardedCookieHeader(
  cookieHeader: string | null,
  session: { accessToken: string; refreshToken?: string | null; user: unknown }
): string {
  const cookies = new Map<string, string>();
  for (const part of cookieHeader?.split(";") ?? []) {
    const separator = part.indexOf("=");
    if (separator <= 0) continue;
    const name = part.slice(0, separator).trim();
    const value = part.slice(separator + 1).trim();
    if (name) cookies.set(name, value);
  }

  cookies.set("pc_at", session.accessToken);
  if (session.refreshToken) cookies.set("pc_rt", session.refreshToken);
  cookies.set("pc_user", encodeURIComponent(JSON.stringify(session.user)));
  return [...cookies.entries()].map(([name, value]) => `${name}=${value}`).join("; ");
}

export function decideRefreshOutcome<T extends { accessToken?: string; user?: unknown }>(
  payload: T
): payload is T & { accessToken: string; user: unknown } {
  return Boolean(payload.accessToken && payload.user);
}
