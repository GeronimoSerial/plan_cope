import "server-only";
import { cookies } from "next/headers";
import type { UserProfile } from "../contracts";

const ACCESS = "pc_at";
const REFRESH = "pc_rt";
const USER = "pc_user";

const ACCESS_MAX_AGE = 60 * 30; // 30 min — alineado con AccessTokenMinutes del backend.
const REFRESH_MAX_AGE = 60 * 60 * 24 * 7; // 7 dias — alineado con RefreshTokenDays.

const baseCookie = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/"
};

export async function setSession(accessToken: string, refreshToken: string | null | undefined, user: UserProfile) {
  const jar = await cookies();
  jar.set(ACCESS, accessToken, { ...baseCookie, maxAge: ACCESS_MAX_AGE });
  if (refreshToken) {
    jar.set(REFRESH, refreshToken, { ...baseCookie, maxAge: REFRESH_MAX_AGE });
  }
  jar.set(USER, JSON.stringify(user), { ...baseCookie, maxAge: REFRESH_MAX_AGE });
}

export async function clearSession() {
  const jar = await cookies();
  for (const name of [ACCESS, REFRESH, USER]) {
    jar.delete(name);
  }
}

export async function getAccessToken(): Promise<string | null> {
  return (await cookies()).get(ACCESS)?.value ?? null;
}

export async function getRefreshToken(): Promise<string | null> {
  return (await cookies()).get(REFRESH)?.value ?? null;
}

export async function getSessionUser(): Promise<UserProfile | null> {
  const raw = (await cookies()).get(USER)?.value;
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as UserProfile;
  } catch {
    return null;
  }
}
