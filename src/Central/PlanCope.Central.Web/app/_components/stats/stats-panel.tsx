"use client";

import { lazy, Suspense, useEffect, useMemo, useState, type ReactNode } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import type { StatsAggregate, StatsAggregateRow, StatsMetric, StatsOption, StatsPage } from "../../_lib/api/server";
import styles from "./stats-panel.module.css";

const StatsBarChart = lazy(() => import("./stats-bar-chart"));
const PAGE_SIZE = 50;

const filters = [
  { key: "departmentId", label: "Departamento", dimension: "department" },
  { key: "localityId", label: "Localidad", dimension: "locality" },
  { key: "course", label: "Grado / curso", dimension: "course" },
  { key: "subject", label: "Materia", dimension: "subject" },
  { key: "schoolYear", label: "Año lectivo", dimension: "year" },
  { key: "school", label: "Establecimiento", dimension: "school" },
  { key: "version", label: "Examen / versión", dimension: "version" },
] as const;

type FilterKey = (typeof filters)[number]["key"];
type FilterValues = Record<FilterKey, string>;
type GroupDimension = "locality" | "department" | "course" | "subject" | "year" | "school" | "version";

const emptyFilters: FilterValues = {
  departmentId: "", localityId: "", course: "", subject: "", schoolYear: "", school: "", version: "",
};

const groupOptions: { value: GroupDimension; label: string }[] = [
  { value: "locality", label: "Localidad" },
  { value: "department", label: "Departamento" },
  { value: "course", label: "Grado / curso" },
  { value: "subject", label: "Materia" },
  { value: "year", label: "Año lectivo" },
  { value: "school", label: "Establecimiento" },
  { value: "version", label: "Examen / versión" },
];

function metricText(metric: StatsMetric<number>, kind: "count" | "percent"): string {
  if (metric.status === "suppressed") return "Cohorte insuficiente";
  if (metric.status === "unavailable" || metric.value === null) return "No disponible";
  return kind === "percent" ? `${metric.value.toFixed(1)}%` : metric.value.toLocaleString("es-AR");
}

function dateText(value: string | null): string {
  if (!value) return "No disponible para esta consulta";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "Fecha no disponible" : new Intl.DateTimeFormat("es-AR", { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function versionCatalogLabel(value: string, label: string, index: number, page: number): string {
  const opaque = label === value || /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(label);
  return opaque ? `Versión ${((page - 1) * PAGE_SIZE) + index + 1} · título no disponible` : label;
}

function aggregateLabel(aggregate: StatsAggregate, index: number): string {
  if (aggregate.dimension !== "version") return aggregate.rows[index].label;
  const row = aggregate.rows[index];
  const labelIsOpaqueId = row.label === row.key || /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(row.label);
  if (!labelIsOpaqueId) return row.label;
  const position = (aggregate.page - 1) * aggregate.pageSize + index + 1;
  return `Versión agrupada ${position} · título no disponible`;
}

function CatalogFilter({ filter, value, onChange }: {
  filter: (typeof filters)[number];
  value: string;
  onChange: (value: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [catalog, setCatalog] = useState<StatsPage<StatsOption> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    const timeout = window.setTimeout(() => {
      setLoading(true);
      setError(false);
      const params = new URLSearchParams({ dimension: filter.dimension, page: String(page), pageSize: String(PAGE_SIZE) });
      if (query.trim()) params.set("query", query.trim());
      void callCentral<StatsPage<StatsOption>>(`stats/catalogs?${params.toString()}`)
        .then(result => { if (active) setCatalog(result); })
        .catch(() => { if (active) setError(true); })
        .finally(() => { if (active) setLoading(false); });
    }, 160);
    return () => { active = false; window.clearTimeout(timeout); };
  }, [filter.dimension, page, query]);

  function updateQuery(next: string) {
    setQuery(next);
    setPage(1);
  }

  const selectedIsVisible = value && catalog?.items.some(item => item.value === value);
  return (
    <div className={styles.filter}>
      <Label htmlFor={`stats-search-${filter.key}`}>{filter.label}</Label>
      <input id={`stats-search-${filter.key}`} type="search" value={query}
        onChange={event => updateQuery(event.target.value)}
        placeholder={`Buscar ${filter.label.toLocaleLowerCase("es-AR")}`}
        aria-label={`Buscar ${filter.label.toLocaleLowerCase("es-AR")}`} className={styles.search} />
      <select value={value} onChange={event => onChange(event.target.value)}
        aria-label={`Seleccionar ${filter.label.toLocaleLowerCase("es-AR")}`} className={styles.select}>
        <option value="">Todos</option>
        {value && !selectedIsVisible ? <option value={value}>{filter.dimension === "version" ? "Versión seleccionada · título no disponible" : value}</option> : null}
        {(catalog?.items ?? []).map((item, index) => <option key={item.value} value={item.value}>
          {filter.dimension === "version" ? versionCatalogLabel(item.value, item.label, index, page) : item.label}
        </option>)}
      </select>
      <div className={styles.catalogFooter}>
        <span aria-live="polite">{loading ? "Buscando…" : error ? "Catálogo no disponible" : `${catalog?.totalCount ?? 0} opciones`}</span>
        <span className={styles.catalogPaging}>
          <button type="button" onClick={() => setPage(current => Math.max(1, current - 1))} disabled={page <= 1 || loading} aria-label={`Página anterior de ${filter.label}`}>‹</button>
          <span>{page}</span>
          <button type="button" onClick={() => setPage(current => current + 1)} disabled={loading || !catalog || page * catalog.pageSize >= catalog.totalCount} aria-label={`Página siguiente de ${filter.label}`}>›</button>
        </span>
      </div>
    </div>
  );
}

function MetricCell({ metric, kind }: { metric: StatsMetric<number>; kind: "count" | "percent" }) {
  const statusLabel = metric.status === "available" ? undefined : metric.status === "suppressed" ? "Valor suprimido por tamaño de cohorte" : "Valor no disponible";
  return <TableCell aria-label={statusLabel}>{metricText(metric, kind)}</TableCell>;
}

function TableShell({ children }: { children: ReactNode }) {
  return <div className={styles.tableShell}>{children}</div>;
}

export function StatsPanel() {
  const [draftFilters, setDraftFilters] = useState<FilterValues>(emptyFilters);
  const [appliedFilters, setAppliedFilters] = useState<FilterValues>(emptyFilters);
  const [groupBy, setGroupBy] = useState<GroupDimension>("course");
  const [page, setPage] = useState(1);
  const [response, setResponse] = useState<{ query: string; aggregate?: StatsAggregate; error?: string } | null>(null);
  const [chartOpen, setChartOpen] = useState(false);

  const query = useMemo(() => {
    const params = new URLSearchParams({ groupBy, page: String(page), pageSize: String(PAGE_SIZE) });
    for (const [key, value] of Object.entries(appliedFilters)) if (value) params.set(key, value);
    return params.toString();
  }, [appliedFilters, groupBy, page]);

  useEffect(() => {
    let active = true;
    void callCentral<StatsAggregate>(`stats/aggregate?${query}`)
      .then(aggregate => { if (active) setResponse({ query, aggregate }); })
      .catch(problem => { if (active) setResponse({ query, error: getErrorMessage(problem, "No se pudieron cargar las estadísticas.") }); });
    return () => { active = false; };
  }, [query]);

  const loading = response?.query !== query;
  const aggregate = loading ? null : response?.aggregate ?? null;
  const error = loading ? null : response?.error ?? null;

  function updateFilter(key: FilterKey, value: string) {
    setDraftFilters(previous => ({ ...previous, [key]: value }));
  }

  function applyFilters() {
    setPage(1);
    setAppliedFilters({ ...draftFilters });
  }

  const chartRows = useMemo(() => (aggregate?.rows ?? []).flatMap((row, index) => {
    if (row.weightedScorePercent.status !== "available" || row.weightedScorePercent.value === null) return [];
    return [{ label: aggregate ? aggregateLabel(aggregate, index) : row.label, score: row.weightedScorePercent.value }];
  }), [aggregate]);

  return (
    <div className={styles.panel}>
      <Card>
        <CardHeader><CardTitle>Filtros de consulta</CardTitle></CardHeader>
        <CardContent className={styles.filtersContent}>
          <div className={styles.filters}>
            {filters.map(filter => <CatalogFilter key={filter.key} filter={filter} value={draftFilters[filter.key]} onChange={value => updateFilter(filter.key, value)} />)}
            <div className={styles.filter}>
              <Label htmlFor="stats-group-by">Agrupar resultados por</Label>
              <select id="stats-group-by" value={groupBy} onChange={event => { setGroupBy(event.target.value as GroupDimension); setPage(1); }} className={styles.select}>
                {groupOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
              <span className={styles.hint}>Una dimensión por consulta</span>
            </div>
          </div>
          <div className={styles.filterActions}>
            <Button onClick={applyFilters}>Aplicar filtros</Button>
            <Button variant="outline" onClick={() => { setDraftFilters(emptyFilters); setAppliedFilters(emptyFilters); setPage(1); }}>Limpiar</Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className={styles.resultsHeader}>
          <div><CardTitle>Resultados agrupados</CardTitle><p className={styles.unit}>Intentos calificados y atribuidos; no representa personas únicas.</p></div>
          <Button type="button" variant="outline" onClick={() => setChartOpen(open => !open)} aria-expanded={chartOpen} aria-controls="stats-chart-region">
            {chartOpen ? "Ocultar gráfico" : "Ver gráfico de barras"}
          </Button>
        </CardHeader>
        <CardContent className={styles.resultsContent}>
          <p className={styles.formula}>Porcentaje ponderado de puntaje = 100 × ΣScore / ΣScoreMax.</p>
          {aggregate && <div className={styles.freshness} aria-label="Frescura de estadísticas">
            <span>Consulta generada: {dateText(aggregate.generatedAt)}</span>
            <span>Último resultado recibido: {dateText(aggregate.latestRollupUpdatedAt)}</span>
          </div>}
          {loading && <p className={styles.state} role="status">Cargando agregados…</p>}
          {!loading && error && <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>}
          {!loading && !error && aggregate?.dataState === "no_results" && <p className={styles.state} role="status">No hay resultados para los filtros aplicados.</p>}
          {!loading && !error && aggregate?.dataState === "available" && aggregate.rows.length === 0 && <p className={styles.state} role="status">No hay filas en esta página. Probá con otra página.</p>}

          <div id="stats-chart-region" className={styles.chartRegion} hidden={!chartOpen || loading || Boolean(error) || aggregate?.dataState !== "available"}>
            {chartOpen && !loading && !error && aggregate?.dataState === "available" && <Suspense fallback={<p className={styles.state} role="status">Cargando gráfico…</p>}><StatsBarChart data={chartRows} /></Suspense>}
          </div>
          {!loading && !error && aggregate?.dataState === "available" && aggregate.rows.length > 0 && <>
            <TableShell>
              <Table>
                <caption className={styles.caption}>Resultados por {groupOptions.find(option => option.value === aggregate.dimension)?.label.toLocaleLowerCase("es-AR") ?? aggregate.dimension}. La tabla contiene los mismos valores disponibles que el gráfico.</caption>
                <TableHeader><TableRow>
                  <TableHead>{groupOptions.find(option => option.value === aggregate.dimension)?.label ?? "Grupo"}</TableHead>
                  <TableHead>Intentos calificados</TableHead>
                  <TableHead>Porcentaje ponderado de puntaje</TableHead>
                </TableRow></TableHeader>
                <TableBody>{aggregate.rows.map((row: StatsAggregateRow, index: number) => <TableRow key={row.key}>
                  <TableCell className={styles.rowLabel}>{aggregateLabel(aggregate, index)}</TableCell>
                  <MetricCell metric={row.attemptCount} kind="count" />
                  <MetricCell metric={row.weightedScorePercent} kind="percent" />
                </TableRow>)}</TableBody>
              </Table>
            </TableShell>
            <nav className={styles.pagination} aria-label="Paginación de resultados">
              <span>{aggregate.totalCount.toLocaleString("es-AR")} grupos · página {aggregate.page}</span>
              <div>
                <Button variant="outline" onClick={() => setPage(current => Math.max(1, current - 1))} disabled={page <= 1 || loading}>Anterior</Button>
                <Button variant="outline" onClick={() => setPage(current => current + 1)} disabled={loading || page * aggregate.pageSize >= aggregate.totalCount}>Siguiente</Button>
              </div>
            </nav>
          </>}
        </CardContent>
      </Card>
    </div>
  );
}
