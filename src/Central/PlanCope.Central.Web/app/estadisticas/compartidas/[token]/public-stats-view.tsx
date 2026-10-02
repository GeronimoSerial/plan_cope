"use client";

import { lazy, Suspense, useEffect, useState } from "react";
import type { PublicStatsShare } from "../../../_lib/shares/contracts";

const StatsBarChart = lazy(() => import("../../../_components/stats/stats-bar-chart"));

function metricText(metric: { value: number | null; status: string }, kind: "count" | "percent") {
  if (metric.status === "suppressed") return "Suprimido por privacidad";
  if (metric.status !== "available" || metric.value === null) return "No disponible";
  return kind === "percent" ? `${metric.value.toFixed(1)}%` : new Intl.NumberFormat("es-AR").format(metric.value);
}

export function PublicStatsView({ token }: { token: string }) {
  const [data, setData] = useState<PublicStatsShare | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "missing" | "error">("loading");
  const [showChart, setShowChart] = useState(false);

  useEffect(() => {
    let active = true;
    fetch(`/api/public/stats/${encodeURIComponent(token)}`, { cache: "no-store" })
      .then(async response => {
        if (response.status === 404) throw new Error("missing");
        if (!response.ok) throw new Error("error");
        return response.json() as Promise<PublicStatsShare>;
      })
      .then(snapshot => { if (active) { setData(snapshot); setState("ready"); } })
      .catch(error => { if (active) setState(error instanceof Error && error.message === "missing" ? "missing" : "error"); });
    return () => { active = false; };
  }, [token]);

  if (state === "loading") return <p role="status">Cargando estadísticas…</p>;
  if (state === "missing") return <p role="status">Este enlace no está disponible.</p>;
  if (state === "error" || !data) return <p role="alert">No se pudieron cargar las estadísticas.</p>;

  const chartData = data.rows.filter(row => row.weightedScorePercent.status === "available" && row.weightedScorePercent.value !== null)
    .map(row => ({ label: row.label, score: row.weightedScorePercent.value! }));
  return <div className="grid gap-6">
    <div className="grid gap-1"><h2 className="text-xl font-semibold">Resultados por {data.groupLabel.toLocaleLowerCase("es-AR")}</h2>
      {Object.entries(data.filters).length > 0 && <p className="text-sm text-muted-foreground">Filtros: {Object.values(data.filters).join(" · ")}</p>}
      <p className="text-xs text-muted-foreground">Generado {new Date(data.generatedAt).toLocaleString("es-AR")} · Vence {new Date(data.expiresAt).toLocaleString("es-AR")}</p>
    </div>
    <section className="grid gap-3 rounded-lg border p-4" aria-label="Totales">
      <h3 className="font-semibold">Total publicado</h3>
      <dl className="grid gap-3 sm:grid-cols-2">
        <div><dt className="text-sm text-muted-foreground">Intentos calificados</dt><dd className="font-medium">{metricText(data.totalAttempts, "count")}</dd></div>
        <div><dt className="text-sm text-muted-foreground">Porcentaje ponderado de puntaje</dt><dd className="font-medium">{metricText(data.totalWeightedScorePercent, "percent")}</dd></div>
      </dl>
      <p className="text-xs text-muted-foreground">Fórmula: 100 × ΣScore / ΣScoreMax. Las cohortes menores a 5 se suprimen.</p>
    </section>
    <section className="grid gap-3">
      <h3 className="font-semibold">Detalle</h3>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-left text-sm">
          <caption className="sr-only">Resultados agregados por {data.groupLabel.toLocaleLowerCase("es-AR")}</caption>
          <thead><tr className="border-b bg-muted/40"><th className="px-3 py-2">{data.groupLabel}</th><th className="px-3 py-2">Intentos calificados</th><th className="px-3 py-2">Porcentaje ponderado</th></tr></thead>
          <tbody>{data.rows.map((row, index) => <tr className="border-b last:border-0" key={`${row.label}-${index}`}>
            <th scope="row" className="px-3 py-2 font-medium">{row.label}</th>
            <td className="px-3 py-2">{metricText(row.attemptCount, "count")}</td>
            <td className="px-3 py-2">{metricText(row.weightedScorePercent, "percent")}</td>
          </tr>)}</tbody>
        </table>
      </div>
      <button className="w-fit rounded-md border px-3 py-2 text-sm font-medium hover:bg-muted" aria-expanded={showChart} onClick={() => setShowChart(value => !value)} type="button">{showChart ? "Ocultar gráfico" : "Mostrar gráfico"}</button>
      {showChart && <Suspense fallback={<p role="status">Cargando gráfico…</p>}><StatsBarChart data={chartData} /></Suspense>}
    </section>
  </div>;
}
