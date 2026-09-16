import { useEffect, useMemo, useState } from "react";
import type { UpdateStatus, UpdateStatusMessage } from "../types";
import { postHostMessage } from "../bridge/nativeBridge";

const initialStatus: UpdateStatus = { state: "idle" };

export function useUpdateStatus() {
  const [status, setStatus] = useState<UpdateStatus>(initialStatus);
  const webview = window.chrome?.webview;

  useEffect(() => {
    if (!webview) {
      return;
    }

    const onMessage = (event: MessageEvent<UpdateStatusMessage>) => {
      if (event.data?.type === "host:updateStatus") {
        setStatus(event.data.status);
      }
    };

    webview.addEventListener("message", onMessage);

    return () => webview.removeEventListener("message", onMessage);
  }, [webview]);

  const checkForUpdates = useMemo(() => () => postHostMessage({ type: "host:checkForUpdates" }), []);
  const confirmRestart = useMemo(() => () => postHostMessage({ type: "host:confirmRestart" }), []);

  return useMemo(
    () => ({ status, checkForUpdates, confirmRestart }),
    [status, checkForUpdates, confirmRestart]
  );
}
