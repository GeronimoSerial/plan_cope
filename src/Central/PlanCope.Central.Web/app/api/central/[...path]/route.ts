import { NextRequest, NextResponse } from "next/server";
import { centralFetch } from "../../../_lib/server/central-server";
import { getAccessToken, clearSession } from "../../../_lib/server/session";
import { refreshSession } from "../../../_lib/server/refresh";

const MAX_PROXY_BODY_BYTES = 4 * 1024 * 1024;

async function readBoundedBody(request: NextRequest): Promise<{ body: string } | { status: 400 | 413; error: string }> {
  const reader = request.body?.getReader();
  if (!reader) return { body: "" };

  const chunks: Uint8Array[] = [];
  let totalBytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      totalBytes += value.byteLength;
      if (totalBytes > MAX_PROXY_BODY_BYTES) {
        await reader.cancel();
        return { status: 413, error: "El cuerpo de la solicitud supera el límite de 4 MB." };
      }
      chunks.push(value);
    }

    const bytes = new Uint8Array(totalBytes);
    let offset = 0;
    for (const chunk of chunks) {
      bytes.set(chunk, offset);
      offset += chunk.byteLength;
    }
    return { body: new TextDecoder("utf-8", { fatal: true }).decode(bytes) };
  } catch {
    return { status: 400, error: "No se pudo leer el cuerpo de la solicitud." };
  } finally {
    reader.releaseLock();
  }
}

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
  const declaredLength = Number(request.headers.get("content-length"));
  if (hasBody && Number.isFinite(declaredLength) && declaredLength > MAX_PROXY_BODY_BYTES) {
    return NextResponse.json({ error: "El cuerpo de la solicitud supera el límite de 4 MB." }, { status: 413 });
  }
  let body: string | undefined;
  if (hasBody) {
    const bodyResult = await readBoundedBody(request);
    if ("status" in bodyResult) {
      return NextResponse.json({ error: bodyResult.error }, { status: bodyResult.status });
    }
    body = bodyResult.body;
  }

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

  return new NextResponse(res.body, {
    status: res.status,
    headers: { "Content-Type": res.headers.get("Content-Type") ?? "application/json" }
  });
}

export { proxy as GET, proxy as POST, proxy as PUT, proxy as PATCH, proxy as DELETE };
