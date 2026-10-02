import type { StatsMetric } from "../../_lib/api/server";

export function StatsMetricValue({ metric, kind = "count" }: { metric: StatsMetric<number>; kind?: "count" | "percent" }) {
  if (metric.status === "suppressed") {
    return <span className="text-muted-foreground">Suprimido</span>;
  }
  if (metric.status === "unavailable" || metric.value === null) {
    return <span className="text-muted-foreground">No disponible</span>;
  }
  const value = kind === "percent"
    ? `${metric.value.toLocaleString("es-AR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
    : metric.value.toLocaleString("es-AR");
  return <span className="tabular-nums">{value}</span>;
}

export function Freshness({ value }: { value: string | null }) {
  if (!value) return <span className="text-muted-foreground">Sin resultados incorporados</span>;
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) return <span className="text-muted-foreground">Fecha no disponible</span>;
  return <time dateTime={timestamp.toISOString()}>{timestamp.toLocaleString("es-AR")}</time>;
}

export function WeightedFormulaNote() {
  return (
    <p className="text-xs text-muted-foreground">
      Porcentaje ponderado de puntaje: 100 × suma de puntajes obtenidos ÷ suma de puntajes máximos. El total cuenta intentos calificados atribuidos, no alumnos ni sesiones.
    </p>
  );
}
