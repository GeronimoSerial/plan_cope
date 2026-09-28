import { NextRequest, NextResponse } from "next/server";
import { centralFetch } from "../../../_lib/server/central-server";
import { getAccessToken, clearSession } from "../../../_lib/server/session";
import { refreshSession } from "../../../_lib/server/refresh";

// Streams the installer from Central's authenticated endpoint. Unlike the generic BFF proxy
// (app/api/central/[...path]/route.ts), this must never read the response body as text or force
// Content-Type: application/json — the installer is binary and can be large, so the body is piped
// through untouched.
export async function GET(request: NextRequest) {
  const channel = request.nextUrl.searchParams.get("channel") ?? "stable";
  let token = await getAccessToken();
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const target = `/api/downloads/installer/file?channel=${encodeURIComponent(channel)}`;
  const send = (accessToken: string) => centralFetch(target, { accessToken });

  let res: Response;
  try {
    res = await send(token);
    if (res.status === 401) {
      const refreshed = await refreshSession();
      if (!refreshed) {
        await clearSession();
        return NextResponse.json({ error: "Sesión expirada." }, { status: 401 });
      }
      token = refreshed;
      res = await send(token);
      if (res.status === 401) {
        await clearSession();
        return NextResponse.json({ error: "Sesión expirada." }, { status: 401 });
      }
    }
  } catch {
    return NextResponse.json({ error: "No se pudo conectar con el servidor." }, { status: 502 });
  }

  if (!res.ok || !res.body) {
    const text = await res.text();
    return new NextResponse(text.length > 0 ? text : null, {
      status: res.status,
      headers: { "Content-Type": "application/json" }
    });
  }

  const headers = new Headers();
  const contentType = res.headers.get("Content-Type");
  const contentDisposition = res.headers.get("Content-Disposition");
  const contentLength = res.headers.get("Content-Length");
  if (contentType) headers.set("Content-Type", contentType);
  if (contentDisposition) headers.set("Content-Disposition", contentDisposition);
  if (contentLength) headers.set("Content-Length", contentLength);

  return new NextResponse(res.body, { status: res.status, headers });
}
