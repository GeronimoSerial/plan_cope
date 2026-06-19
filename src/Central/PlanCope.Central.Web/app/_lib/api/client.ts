import { extractApiError } from "../json";

// Cliente del navegador. SIEMPRE habla con el BFF (/api/central/...), nunca con el Central API.
// El token vive en cookies httpOnly y lo adjunta el proxy del servidor.
export async function callCentral<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(`/api/central/${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init.headers }
  });

  if (res.status === 401) {
    if (typeof window !== "undefined") {
      window.location.href = "/login?expired=1";
    }
    throw new Error("Tu sesion expiro. Volve a ingresar.");
  }

  const text = await res.text();
  if (!res.ok) {
    throw new Error(extractApiError(text, res.status));
  }

  return (text ? (JSON.parse(text) as T) : (undefined as T));
}
