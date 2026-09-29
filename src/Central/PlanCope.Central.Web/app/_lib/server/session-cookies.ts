export const ACCESS_COOKIE = "pc_at";
export const REFRESH_COOKIE = "pc_rt";
export const USER_COOKIE = "pc_user";
export const REFRESH_ATTEMPT_COOKIE = "pc_refresh_attempted";

export const ACCESS_MAX_AGE = 60 * 30;
export const REFRESH_MAX_AGE = 60 * 60 * 24 * 7;

export interface SessionCookieOptions {
  httpOnly: boolean;
  secure: boolean;
  sameSite: "lax";
  path: string;
  maxAge: number;
}

export const SESSION_COOKIE_OPTIONS = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/"
};

interface CookieSetter {
  set(name: string, value: string, options: SessionCookieOptions): unknown;
}

export function setSessionCookies(
  target: CookieSetter,
  session: { accessToken: string; refreshToken?: string | null; user: unknown }
): void {
  target.set(ACCESS_COOKIE, session.accessToken, { ...SESSION_COOKIE_OPTIONS, maxAge: ACCESS_MAX_AGE });
  if (session.refreshToken) {
    target.set(REFRESH_COOKIE, session.refreshToken, { ...SESSION_COOKIE_OPTIONS, maxAge: REFRESH_MAX_AGE });
  }
  target.set(USER_COOKIE, JSON.stringify(session.user), { ...SESSION_COOKIE_OPTIONS, maxAge: REFRESH_MAX_AGE });
}

export function clearSessionCookies(target: CookieSetter): void {
  for (const name of [ACCESS_COOKIE, REFRESH_COOKIE, USER_COOKIE, REFRESH_ATTEMPT_COOKIE]) {
    target.set(name, "", { ...SESSION_COOKIE_OPTIONS, maxAge: 0 });
  }
}

export function clearSessionAttemptCookie(target: CookieSetter): void {
  target.set(REFRESH_ATTEMPT_COOKIE, "", { ...SESSION_COOKIE_OPTIONS, maxAge: 0 });
}

export function setSessionAttemptCookie(target: CookieSetter): void {
  target.set(REFRESH_ATTEMPT_COOKIE, "1", { ...SESSION_COOKIE_OPTIONS, maxAge: 60 });
}
