import type { HostWebViewBridge } from "./types";

type StatsReportMessage = {
  type: "host:statsReportResult";
  requestId: string;
  success: boolean;
  path?: string;
  message: string;
};

export type StatsReportFilters = {
  cue: string;
  schoolYear?: string;
  course?: string;
  exam?: string;
};

export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

export async function openStatsReport(
  filters: StatsReportFilters,
  fetchReport: () => Promise<Blob>
): Promise<string> {
  const webview: HostWebViewBridge | undefined = window.chrome?.webview;
  if (!webview) {
    const blob = await fetchReport();
    downloadBlob(blob, `informe-estadistico-${filters.cue.replace(/[^a-zA-Z0-9_-]/g, "_")}.html`);
    return "Informe HTML descargado.";
  }

  const requestId = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timeout = window.setTimeout(() => {
      webview.removeEventListener("message", onMessage);
      reject(new Error("El host no respondió a la solicitud del informe."));
    }, 30000);
    const onMessage = (event: MessageEvent<StatsReportMessage>) => {
      if (event.data?.type !== "host:statsReportResult" || event.data.requestId !== requestId) return;
      window.clearTimeout(timeout);
      webview.removeEventListener("message", onMessage);
      if (event.data.success) resolve(event.data.message);
      else reject(new Error(event.data.message || "No se pudo generar el informe HTML."));
    };
    webview.addEventListener("message", onMessage);
    webview.postMessage({ type: "host:openStatsReport", requestId, ...filters });
  });
}
