import { NextRequest, NextResponse } from "next/server";
import { centralFetch } from "../../../_lib/server/central-server";
import { getAccessToken, clearSession } from "../../../_lib/server/session";
import { refreshSession } from "../../../_lib/server/refresh";

// Proxy autenticado generico: el navegador llama /api/central/<ruta del API> y este handler
// adjunta el Bearer desde la cookie httpOnly. Si el API responde 401, intenta refresh una vez.
async function proxy(request: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  let token = await getAccessToken();
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const target = `/api/${path.map(encodeURIComponent).join("/")}${request.nextUrl.search}`;
  const method = request.method;
  const hasBody = method !== "GET" && method !== "HEAD";
  const body = hasBody ? await request.text() : undefined;

  const send = (accessToken: string) =>
    centralFetch(target, {
      method,
      headers: hasBody ? { "Content-Type": "application/json" } : undefined,
      body,
      accessToken
    });

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

  const text = await res.text();
  return new NextResponse(text.length > 0 ? text : null, {
    status: res.status,
    headers: { "Content-Type": "application/json" }
  });
}

export { proxy as GET, proxy as POST, proxy as PUT, proxy as PATCH, proxy as DELETE };
