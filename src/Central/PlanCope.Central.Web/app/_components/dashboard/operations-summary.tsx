import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { StatsMetric, StatsSummary } from "../../_lib/api/server";

type StatsState = { summary: StatsSummary } | { unavailable: "denied" | "error" };

interface OperationsSummaryProps {
  state: StatsState;
  publishedExams: number | null;
}

interface MetricView {
  label: string;
  detail: string;
  metric: StatsMetric<number> | null;
  fallback?: number | null;
}

function formatMetric(metric: StatsMetric<number> | null, fallback?: number | null): { value: string; detail: string } {
  if (fallback !== undefined && fallback !== null) {
    return { value: fallback.toLocaleString("es-AR"), detail: "Según los exámenes de tu alcance." };
  }
  if (!metric || metric.status === "unavailable") return { value: "—", detail: "Dato no disponible para este alcance." };
  if (metric.status === "suppressed") return { value: "—", detail: "Dato suprimido por la política de privacidad." };
  if (metric.value === null) return { value: "—", detail: "Dato no disponible." };
  return { value: metric.value.toLocaleString("es-AR"), detail: "Conteo agregado." };
}

function updatedLabel(value: string | null): string {
  if (!value) return "Sin resultados incorporados todavía.";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Fecha de actualización no disponible." : `Último resultado incorporado: ${date.toLocaleString("es-AR")}.`;
}

export function OperationsSummary({ state, publishedExams }: OperationsSummaryProps) {
  const summary = "summary" in state ? state.summary : null;
  const metrics: MetricView[] = [
    { label: "Exámenes publicados", detail: "Publicados dentro del alcance autorizado.", metric: summary?.publishedExams ?? null, fallback: publishedExams },
    { label: "Escuelas con resultados", detail: "Establecimientos con al menos un intento calificado.", metric: summary?.schoolsWithResults ?? null },
    { label: "Intentos calificados", detail: "Intentos corregidos y atribuidos a sus rollups.", metric: summary?.gradedAttempts ?? null },
    { label: "Sesiones frescas", detail: "Sesiones activas o pausadas con heartbeat vigente.", metric: summary?.freshSessions ?? null },
    ...(summary?.pendingAttribution !== null && summary?.pendingAttribution !== undefined
      ? [{ label: "Pendientes de atribución", detail: "Pendientes visibles para tu alcance autorizado.", metric: summary.pendingAttribution }]
      : [])
  ];

  return (
    <section aria-labelledby="operations-summary-heading" className="mb-6">
      <div className="mb-3 flex flex-wrap items-end justify-between gap-2">
        <div>
          <h2 id="operations-summary-heading" className="text-lg font-semibold tracking-tight">Actividad y resultados</h2>
          <p className="text-sm text-muted-foreground">Las sesiones en curso y los resultados sincronizados se contabilizan por separado.</p>
        </div>
        {!summary && <p role="status" className="text-sm text-muted-foreground">{"unavailable" in state && state.unavailable === "denied" ? "Las estadísticas no están habilitadas para este alcance." : "No se pudieron cargar las estadísticas."}</p>}
      </div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        {metrics.map(({ label, detail, metric, fallback }) => {
          const formatted = formatMetric(metric, fallback);
          return (
            <Card key={label}>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-semibold tabular-nums" aria-label={`${label}: ${formatted.value}`}>{formatted.value}</p>
                <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
                {formatted.detail !== "Conteo agregado." && <p className="mt-1 text-xs text-muted-foreground">{formatted.detail}</p>}
              </CardContent>
            </Card>
          );
        })}
      </div>
      {summary && (
        <p className="mt-3 text-xs text-muted-foreground" aria-label="Frescura de estadísticas">
          {updatedLabel(summary.latestRollupUpdatedAt)} Conteo de sesiones calculado al {new Date(summary.generatedAt).toLocaleString("es-AR")}.
        </p>
      )}
    </section>
  );
}
