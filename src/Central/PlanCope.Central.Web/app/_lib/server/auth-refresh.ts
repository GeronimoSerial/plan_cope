import "server-only";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { decideExpiredPage } from "./auth-decision";

// Server Components cannot persist refreshed cookies, so retry through the route handler.
export async function redirectAfterSessionExpired(nextPath: string): Promise<never> {
  const attempted = Boolean((await cookies()).get("pc_refresh_attempted")?.value);
  if (decideExpiredPage(attempted) === "login") {
    redirect("/login?expired=1");
  }
  redirect(`/api/session/refresh?next=${encodeURIComponent(nextPath)}`);
}
