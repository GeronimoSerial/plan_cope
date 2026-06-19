import { useEffect, useMemo, useState } from "react";
import type { HostContext, HostContextMessage, NativeBridge } from "../types";

declare global {
  interface Window {
    chrome?: {
      webview?: NativeBridge & {
        addEventListener: (type: "message", listener: (event: MessageEvent) => void) => void;
        removeEventListener: (type: "message", listener: (event: MessageEvent) => void) => void;
      };
    };
  }
}

const fallbackContext: HostContext = {
  apiBaseUrl: "http://127.0.0.1:5055",
  lanBaseUrl: "http://127.0.0.1:5055",
  operatorName: "Operador",
  port: 5055
};

export function useHostContext(): HostContext {
  const [context, setContext] = useState<HostContext>(fallbackContext);
  const webview = window.chrome?.webview;

  useEffect(() => {
    if (!webview) {
      return;
    }

    const onMessage = (event: MessageEvent<HostContextMessage>) => {
      if (event.data?.type === "host:context") {
        setContext(event.data.context);
      }
    };

    webview.addEventListener("message", onMessage);
    webview.postMessage({ type: "host:ready" });

    return () => webview.removeEventListener("message", onMessage);
  }, [webview]);

  return useMemo(() => context, [context]);
}
