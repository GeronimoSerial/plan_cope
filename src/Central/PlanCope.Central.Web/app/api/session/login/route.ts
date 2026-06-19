import { NextResponse } from "next/server";
import { z } from "zod";
import { centralFetch } from "../../../_lib/server/central-server";
import { setSession } from "../../../_lib/server/session";
import type { LoginResponse } from "../../../_lib/contracts";

const bodySchema = z.object({
  username: z.string().trim().min(1, "Ingresa tu usuario."),
  password: z.string().min(1, "Ingresa tu contraseña.")
});

export async function POST(request: Request) {
  const parsed = bodySchema.safeParse(await request.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: parsed.error.issues[0]?.message ?? "Datos inválidos." }, { status: 400 });
  }

  let res: Response;
  try {
    res = await centralFetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(parsed.data)
    });
  } catch {
    return NextResponse.json({ error: "No se pudo conectar con el servidor. Intenta más tarde." }, { status: 502 });
  }

  if (res.status === 401) {
    return NextResponse.json({ error: "Usuario o contraseña incorrectos." }, { status: 401 });
  }
  if (!res.ok) {
    return NextResponse.json({ error: "No se pudo iniciar sesión." }, { status: 502 });
  }

  const payload = (await res.json()) as LoginResponse;
  if (!payload.accessToken || !payload.user) {
    return NextResponse.json({ error: "Respuesta de autenticación inválida." }, { status: 502 });
  }

  await setSession(payload.accessToken, payload.refreshToken, payload.user);
  return NextResponse.json({ user: payload.user });
}
