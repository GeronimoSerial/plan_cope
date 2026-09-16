import { FormEvent, useEffect, useState } from "react";
import type { NativeBridge } from "../types";

type ActivationScreenProps = {
  apiBaseUrl: string;
  bridge?: NativeBridge;
};

type StoredPassphraseMessage = {
  type: "host:storedPassphrase";
  passphrase?: string;
};

type BundleCuesResponse = {
  cues?: string[];
};

type UnlockErrorResponse = {
  error?: string;
};

export function isValidPassphrase(value: string): boolean {
  return value.trim().length > 0;
}

export function shouldShowActivation(isActivated: boolean): boolean {
  return !isActivated;
}

export function ActivationScreen({
  apiBaseUrl,
  bridge = window.chrome?.webview
}: ActivationScreenProps) {
  const [passphrase, setPassphrase] = useState("");
  const [cue, setCue] = useState("");
  const [cues, setCues] = useState<string[] | null>(null);
  const [cuesError, setCuesError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setCuesError(null);

    fetch(`${apiBaseUrl}/api/activation/bundle-cues`)
      .then(response => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return response.json() as Promise<BundleCuesResponse>;
      })
      .then(data => {
        if (cancelled) {
          return;
        }
        const list = Array.isArray(data.cues) ? data.cues : [];
        setCues(list);
        if (list.length === 1) {
          setCue(list[0]);
        }
      })
      .catch(() => {
        if (cancelled) {
          return;
        }
        setCuesError(
          "No se pudieron obtener los CUE disponibles. Verificá la conexión con el servicio local e intentá nuevamente."
        );
      });

    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl]);

  useEffect(() => {
    if (!bridge) {
      return;
    }

    bridge.postMessage({ type: "host:getStoredPassphrase" });

    const webview = window.chrome?.webview;
    if (!webview) {
      return;
    }

    const onMessage = (event: MessageEvent<StoredPassphraseMessage>) => {
      if (
        event.data?.type === "host:storedPassphrase" &&
        typeof event.data.passphrase === "string" &&
        event.data.passphrase.trim().length > 0
      ) {
        setPassphrase(event.data.passphrase);
      }
    };

    webview.addEventListener("message", onMessage);

    return () => webview.removeEventListener("message", onMessage);
  }, [bridge]);

  const activate = async (event: FormEvent) => {
    event.preventDefault();
    if (!isValidPassphrase(passphrase) || !cue || !bridge || submitted) {
      return;
    }

    setSubmitError(null);
    setSubmitted(true);

    try {
      const response = await fetch(`${apiBaseUrl}/api/activation/unlock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ passphrase, cue })
      });

      if (!response.ok) {
        let message = "No se pudo completar la activación. Intentá nuevamente.";
        try {
          const body = (await response.json()) as UnlockErrorResponse;
          if (typeof body.error === "string" && body.error.trim().length > 0) {
            message = body.error;
          }
        } catch {
          // Keep the generic message when the body is not valid JSON.
        }
        setSubmitError(message);
        setSubmitted(false);
        return;
      }

      bridge.postMessage({ type: "host:activationComplete", passphrase });
    } catch {
      setSubmitError("No se pudo completar la activación. Intentá nuevamente.");
      setSubmitted(false);
    }
  };

  const isLoadingCues = cues === null && !cuesError;

  return (
    <main className="school-gate">
      <form className="gate-card activation-card" onSubmit={activate}>
        <p className="eyebrow">Activación del equipo</p>
        <h1>Activar Plan Cope Local</h1>
        <p>
          Ingresá la frase secreta entregada al responsable. Se solicitará una única vez y
          quedará protegida para este usuario de Windows.
        </p>
        <label htmlFor="activation-passphrase">Frase secreta</label>
        <input
          id="activation-passphrase"
          name="passphrase"
          type="password"
          autoComplete="new-password"
          value={passphrase}
          disabled={submitted}
          onChange={event => setPassphrase(event.target.value)}
        />
        <label htmlFor="activation-cue">CUE</label>
        {isLoadingCues && <p>Obteniendo los CUE disponibles…</p>}
        {cuesError && <p role="alert">{cuesError}</p>}
        {cues && cues.length === 0 && (
          <p role="alert">No hay ningún CUE configurado en este equipo.</p>
        )}
        {cues && cues.length > 0 && (
          <select
            id="activation-cue"
            name="cue"
            value={cue}
            disabled={submitted}
            onChange={event => setCue(event.target.value)}
          >
            <option value="" disabled>
              Seleccioná un CUE
            </option>
            {cues.map(option => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        )}
        <button
          type="submit"
          disabled={!isValidPassphrase(passphrase) || !cue || submitted || !bridge}
        >
          {submitted ? "Activando…" : "Activar equipo"}
        </button>
        {submitError && <p role="alert">{submitError}</p>}
        {!bridge && (
          <p role="alert">
            La activación sólo está disponible dentro de la aplicación de escritorio.
          </p>
        )}
      </form>
    </main>
  );
}
