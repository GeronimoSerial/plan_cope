import { FormEvent, useState } from "react";
import type { NativeBridge } from "../types";

type ActivationScreenProps = {
  bridge?: NativeBridge;
};

export function isValidPassphrase(value: string): boolean {
  return value.trim().length > 0;
}

export function shouldShowActivation(isActivated: boolean): boolean {
  return !isActivated;
}

export function ActivationScreen({ bridge = window.chrome?.webview }: ActivationScreenProps) {
  const [passphrase, setPassphrase] = useState("");
  const [submitted, setSubmitted] = useState(false);

  const activate = (event: FormEvent) => {
    event.preventDefault();
    if (!isValidPassphrase(passphrase) || !bridge) {
      return;
    }

    bridge.postMessage({ type: "host:activate", passphrase });
    setSubmitted(true);
  };

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
        <button type="submit" disabled={!isValidPassphrase(passphrase) || submitted || !bridge}>
          {submitted ? "Activando…" : "Activar equipo"}
        </button>
        {!bridge && <p role="alert">La activación sólo está disponible dentro de la aplicación de escritorio.</p>}
      </form>
    </main>
  );
}
