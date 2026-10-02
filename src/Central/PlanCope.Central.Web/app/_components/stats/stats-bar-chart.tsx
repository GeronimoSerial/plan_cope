"use client";

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

export interface StatsChartPoint { label: string; score: number }

export default function StatsBarChart({ data }: { data: StatsChartPoint[] }) {
  const summary = data.length
    ? `Porcentaje ponderado de puntaje por grupo: ${data.map(item => `${item.label}, ${item.score.toFixed(1)} por ciento`).join("; ")}. Los valores suprimidos o no disponibles se leen en la tabla.`
    : "No hay porcentajes disponibles para graficar. Los valores suprimidos o no disponibles se leen en la tabla.";

  return (
    <figure aria-label={summary} className="grid gap-2">
      <figcaption className="text-sm font-semibold">Porcentaje ponderado de puntaje</figcaption>
      {data.length ? <div className="h-64 w-full" role="img" aria-label={summary}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} margin={{ top: 12, right: 16, bottom: 46, left: 8 }} accessibilityLayer>
            <CartesianGrid stroke="var(--line-soft)" vertical={false} />
            <XAxis dataKey="label" angle={-28} textAnchor="end" interval={0} height={62} tick={{ fill: "var(--ink-soft)", fontSize: 12 }} />
            <YAxis domain={[0, 100]} unit="%" width={48} tick={{ fill: "var(--ink-soft)", fontSize: 12 }} />
            <Tooltip formatter={value => [`${Number(value).toFixed(1)}%`, "Puntaje ponderado"]} />
            <Bar dataKey="score" name="Puntaje ponderado" fill="var(--link-accent)" maxBarSize={54} radius={[2, 2, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div> : <p className="text-sm text-muted-foreground">No hay porcentajes disponibles para graficar.</p>}
    </figure>
  );
}
