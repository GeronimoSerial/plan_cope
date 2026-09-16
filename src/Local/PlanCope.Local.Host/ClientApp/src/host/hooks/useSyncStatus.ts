import { useEffect, useState } from "react";
import { ApiClient, type SyncStatusDto } from "../api/apiClient";

const SYNC_STATUS_POLL_MS = 30000;

export function useSyncStatus(apiBaseUrl: string): SyncStatusDto | null {
  const [status, setStatus] = useState<SyncStatusDto | null>(null);

  useEffect(() => {
    if (!apiBaseUrl) {
      return;
    }

    const api = new ApiClient(apiBaseUrl);
    let pending: AbortController | null = null;

    const poll = async () => {
      if (document.visibilityState !== "visible") {
        return;
      }

      pending?.abort();
      const tickController = new AbortController();
      pending = tickController;

      try {
        const next = await api.getSyncStatus(tickController.signal);
        if (!tickController.signal.aborted) {
          setStatus(next);
        }
      } catch {
        // Transient poll failures keep the last known good state; a flaky
        // request must not itself look like a sync failure.
      } finally {
        if (pending === tickController) {
          pending = null;
        }
      }
    };

    void poll();
    const interval = setInterval(() => void poll(), SYNC_STATUS_POLL_MS);

    return () => {
      clearInterval(interval);
      pending?.abort();
    };
  }, [apiBaseUrl]);

  return status;
}