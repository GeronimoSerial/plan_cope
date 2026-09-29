import { cookies } from "next/headers";
import { NextRequest, NextResponse } from "next/server";
import { refreshSession } from "../../../_lib/server/refresh";
import { clearSessionCookies, setSessionAttemptCookie } from "../../../_lib/server/session-cookies";
import { safeInternalPath } from "../../../_lib/safe-redirect";

export async function POST() {
  const accessToken = await refreshSession();
  if (!accessToken) {
    return NextResponse.json({ error: "Sesión expirada." }, { status: 401 });
  }
  return NextResponse.json({ ok: true });
}

// Refresh runs in a Route Handler so renewed HttpOnly cookies reach the browser.
export async function GET(request: NextRequest) {
  const jar = await cookies();
  const alreadyAttempted = jar.get("pc_refresh_attempted")?.value === "1";
  if (alreadyAttempted) {
    const response = NextResponse.redirect(new URL("/login?expired=1", request.url));
    clearSessionCookies(response.cookies);
    return response;
  }

  const accessToken = await refreshSession();
  if (!accessToken) {
    const response = NextResponse.redirect(new URL("/login?expired=1", request.url));
    clearSessionCookies(response.cookies);
    return response;
  }

  const response = NextResponse.redirect(new URL(safeInternalPath(request.nextUrl.searchParams.get("next"), request.nextUrl.origin), request.url));
  setSessionAttemptCookie(response.cookies);
  return response;
}
