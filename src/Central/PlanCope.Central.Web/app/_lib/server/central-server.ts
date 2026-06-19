import "server-only";

const baseUrl = (process.env.CENTRAL_API_URL ?? "https://localhost:7088").replace(/\/$/, "");

// En desarrollo el Central API corre con certificado self-signed (https://localhost:7058).
// Aceptamos ese certificado SOLO fuera de produccion. Nunca en produccion.
if (process.env.NODE_ENV !== "production" && process.env.CENTRAL_API_ALLOW_INSECURE !== "false") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

interface CentralFetchInit extends RequestInit {
  accessToken?: string | null;
}

export function centralBaseUrl() {
  return baseUrl;
}

export async function centralFetch(path: string, init: CentralFetchInit = {}): Promise<Response> {
  const { accessToken, ...rest } = init;
  const headers = new Headers(rest.headers);
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  return fetch(`${baseUrl}${path}`, { ...rest, headers, cache: "no-store" });
}
