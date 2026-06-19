import { NextResponse } from "next/server";
import { refreshSession } from "../../../_lib/server/refresh";

export async function POST() {
  const accessToken = await refreshSession();
  if (!accessToken) {
    return NextResponse.json({ error: "Sesión expirada." }, { status: 401 });
  }
  return NextResponse.json({ ok: true });
}
