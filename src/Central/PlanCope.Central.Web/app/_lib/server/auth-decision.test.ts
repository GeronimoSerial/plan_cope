import { describe, expect, it } from "vitest";
import { decideProxyAuth, decideLoginAuth, decideExpiredPage, decideRefreshOutcome, buildForwardedCookieHeader } from "./auth-decision";

describe("proxy auth decision", () => {
  it("redirects and clears when no cookies exist", () => {
    expect(decideProxyAuth([])).toEqual({ kind: "redirect-clear" });
  });

  it("does not treat a profile cookie as a session", () => {
    expect(decideProxyAuth(["pc_user"])).toEqual({ kind: "redirect-clear" });
  });

  it("refreshes when only a refresh cookie exists", () => {
    expect(decideProxyAuth(["pc_rt"])).toEqual({ kind: "refresh" });
  });

  it("continues when an access cookie exists", () => {
    expect(decideProxyAuth(["pc_at", "pc_rt", "pc_user"])).toEqual({ kind: "next" });
  });

});

describe("login auth decision", () => {
  it("only redirects an active access session when not expired", () => {
    expect(decideLoginAuth({ hasAccess: true, expired: false })).toBe(true);
    expect(decideLoginAuth({ hasAccess: true, expired: true })).toBe(false);
    expect(decideLoginAuth({ hasAccess: false, expired: false })).toBe(false);
  });
});


describe("expired page decision", () => {
  it("refreshes once, then logs out after a repeated 401", () => {
    expect(decideExpiredPage(false)).toBe("refresh");
    expect(decideExpiredPage(true)).toBe("login");
  });
});


describe("forwarded session cookies", () => {
  it("replaces incoming session cookies and preserves unrelated cookies", () => {
    expect(buildForwardedCookieHeader("theme=dark; pc_at=old; pc_rt=old-refresh; pc_user=old-user", {
      accessToken: "new-access",
      refreshToken: "new-refresh",
      user: { id: "user-1" }
    })).toBe('theme=dark; pc_at=new-access; pc_rt=new-refresh; pc_user=%7B%22id%22%3A%22user-1%22%7D');
  });
});


describe("refresh outcome", () => {
  it("clears the session when refresh does not return a complete session", () => {
    expect(decideRefreshOutcome({ accessToken: undefined, user: undefined })).toBe(false);
    expect(decideRefreshOutcome({ accessToken: "fresh-access", user: { id: "user-1" } })).toBe(true);
  });
});
