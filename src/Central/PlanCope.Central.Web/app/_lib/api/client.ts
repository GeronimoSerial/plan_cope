import { extractApiError } from "../json";
import { examWriteErrorMessage } from "../exam-permissions";

// Error del Central API con el estado HTTP y el cuerpo ya parseado, para que la UI pueda reaccionar
// a codigos concretos (por ejemplo el 409 "draft_exists" al crear una version).
export class CentralApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(message: string, status: number, body: unknown) {
    super(message);
    this.name = "CentralApiError";
    this.status = status;
    this.body = body;
  }
}

function parseJsonBody(text: string): unknown {
  if (!text) {
    return undefined;
  }
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

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
    throw new Error("Tu sesión expiró. Volvé a ingresar.");
  }

  const text = await res.text();
  if (!res.ok) {
    const message = extractApiError(text, res.status);
    throw new CentralApiError(examWriteErrorMessage(res.status, message), res.status, parseJsonBody(text));
  }

  return (text ? (JSON.parse(text) as T) : (undefined as T));
}
