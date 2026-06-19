export function postHostMessage(message: unknown): void {
  window.chrome?.webview?.postMessage(message);
}
