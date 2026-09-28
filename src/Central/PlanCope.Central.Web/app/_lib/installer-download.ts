// Same-origin path the Descargas page links to. Central's own download endpoint requires a
// Bearer token the browser cannot attach to a plain link; this app's own route (route.ts next to
// this file's usage) reads the session cookie server-side and streams Central's response through.
export function buildInstallerDownloadHref(channel: string): string {
  return `/api/installer/download?channel=${encodeURIComponent(channel)}`;
}
