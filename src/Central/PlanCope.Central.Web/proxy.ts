import { NextResponse, type NextRequest } from "next/server";
import { buildForwardedCookieHeader, decideProxyAuth, decideRefreshOutcome, isPublicSharedStatsPath } from "./app/_lib/server/auth-decision";
import { clearSessionCookies, clearSessionAttemptCookie, setSessionCookies } from "./app/_lib/server/session-cookies";

function redirectToLogin(request: NextRequest) {
  const url = new URL("/login", request.url);
  url.searchParams.set("from", request.nextUrl.pathname);
  const response = NextResponse.redirect(url);
  clearSessionCookies(response.cookies);
  return response;
}

export async function proxy(request: NextRequest) {
  const isLogin = request.nextUrl.pathname === "/login";
  if (isPublicSharedStatsPath(request.nextUrl.pathname)) {
    const response = NextResponse.next();
    response.headers.set("Cache-Control", "private, no-store, max-age=0");
    response.headers.set("Referrer-Policy", "no-referrer");
    response.headers.set("X-Robots-Tag", "noindex, nofollow");
    return response;
  }
  if (isLogin) {
    if (request.nextUrl.searchParams.get("expired") === "1") {
      const response = NextResponse.next();
      clearSessionCookies(response.cookies);
      return response;
    }
    return NextResponse.next();
  }

  const decision = decideProxyAuth(request.cookies.getAll().map(cookie => cookie.name));
  if (decision.kind === "next") {
    const response = NextResponse.next();
    clearSessionAttemptCookie(response.cookies);
    return response;
  }
  if (decision.kind === "redirect-clear") return redirectToLogin(request);

  let refreshResponse: Response;
  try {
    const baseUrl = (process.env.CENTRAL_API_URL ?? "https://localhost:7088").replace(/\/$/, "");
    refreshResponse = await fetch(`${baseUrl}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: request.cookies.get("pc_rt")?.value }),
      cache: "no-store"
    });
  } catch {
    return redirectToLogin(request);
  }

  if (!refreshResponse.ok) return redirectToLogin(request);
  let payload: { accessToken?: string; refreshToken?: string | null; user?: unknown };
  try {
    payload = await refreshResponse.json();
  } catch {
    return redirectToLogin(request);
  }
  if (!decideRefreshOutcome(payload)) {
    return redirectToLogin(request);
  }

  const session = { accessToken: payload.accessToken, refreshToken: payload.refreshToken, user: payload.user };
  const headers = new Headers(request.headers);
  headers.set("cookie", buildForwardedCookieHeader(request.headers.get("cookie"), session));
  const response = NextResponse.next({ request: { headers } });
  setSessionCookies(response.cookies, session);
  return response;
}

export const config = {
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|map|txt|xml)$).*)"]
};
