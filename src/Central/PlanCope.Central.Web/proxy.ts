import { NextResponse, type NextRequest } from "next/server";

// Protege el grupo (app). Sin cookie de sesion -> redirige a /login conservando el destino.
export function proxy(request: NextRequest) {
  const hasSession = request.cookies.has("pc_at");
  if (!hasSession) {
    const url = new URL("/login", request.url);
    url.searchParams.set("from", request.nextUrl.pathname);
    return NextResponse.redirect(url);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/dashboard/:path*", "/exams/:path*"]
};
