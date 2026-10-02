import { NextResponse } from "next/server";
import { centralFetch } from "../../../../_lib/server/central-server";

export async function GET(_request: Request, { params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  if (!/^[A-Za-z0-9_-]{43}$/.test(token)) return new NextResponse(null, { status: 404, headers: { "Cache-Control": "no-store, max-age=0" } });
  try {
    const response = await centralFetch(`/api/public/stats/${encodeURIComponent(token)}`);
    return new NextResponse(response.body, {
      status: response.status,
      headers: {
        "Content-Type": response.headers.get("Content-Type") ?? "application/json",
        "Cache-Control": "private, no-store, max-age=0",
        "Referrer-Policy": "no-referrer",
        "X-Robots-Tag": "noindex, nofollow",
      },
    });
  } catch {
    return NextResponse.json({ error: "No se pudo cargar el enlace." }, { status: 502, headers: { "Cache-Control": "no-store" } });
  }
}
