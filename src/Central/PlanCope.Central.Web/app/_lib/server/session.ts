import "server-only";
import { cookies } from "next/headers";
import type { UserProfile } from "../contracts";
import {
  ACCESS_COOKIE,
  REFRESH_COOKIE,
  USER_COOKIE,
  clearSessionCookies,
  setSessionCookies
} from "./session-cookies";

export async function setSession(accessToken: string, refreshToken: string | null | undefined, user: UserProfile) {
  const jar = await cookies();
  setSessionCookies(jar, { accessToken, refreshToken, user });
}

export async function clearSession() {
  clearSessionCookies(await cookies());
}

export async function getAccessToken(): Promise<string | null> {
  return (await cookies()).get(ACCESS_COOKIE)?.value ?? null;
}

export async function getRefreshToken(): Promise<string | null> {
  return (await cookies()).get(REFRESH_COOKIE)?.value ?? null;
}

export async function getSessionUser(): Promise<UserProfile | null> {
  const raw = (await cookies()).get(USER_COOKIE)?.value;
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as UserProfile;
  } catch {
    return null;
  }
}
