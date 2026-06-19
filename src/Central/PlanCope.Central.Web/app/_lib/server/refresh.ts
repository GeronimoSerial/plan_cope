import "server-only";
import { centralFetch } from "./central-server";
import { getRefreshToken, setSession, clearSession } from "./session";
import type { LoginResponse } from "../contracts";

// Intenta renovar la sesion server-side con el refresh token guardado en cookie.
// Devuelve el nuevo access token, o null si no se pudo (y limpia la sesion).
export async function refreshSession(): Promise<string | null> {
  const refreshToken = await getRefreshToken();
  if (!refreshToken) {
    return null;
  }

  let res: Response;
  try {
    res = await centralFetch("/api/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken })
    });
  } catch {
    return null;
  }

  if (!res.ok) {
    await clearSession();
    return null;
  }

  const payload = (await res.json()) as LoginResponse;
  if (!payload.accessToken || !payload.user) {
    await clearSession();
    return null;
  }

  await setSession(payload.accessToken, payload.refreshToken, payload.user);
  return payload.accessToken;
}
