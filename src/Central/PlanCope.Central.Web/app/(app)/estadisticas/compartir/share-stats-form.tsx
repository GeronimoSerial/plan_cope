"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { callCentral } from "../../../_lib/api/client";
import { createStatsShare, revokeStatsShare } from "../../../_lib/shares/client";
import type { StatsShareCreated } from "../../../_lib/shares/contracts";

type Dimension = "department" | "locality" | "year" | "course" | "subject";
type FilterKey = "departmentId" | "localityId" | "schoolYear" | "course" | "subject";
type CatalogItem = { value: string; label: string };
type CatalogPage = { page: number; pageSize: number; totalCount: number; items: CatalogItem[] };

const filters: { key: FilterKey; dimension: Dimension; label: string }[] = [
  { key: "departmentId", dimension: "department", label: "Departamento" },
  { key: "localityId", dimension: "locality", label: "Localidad" },
  { key: "schoolYear", dimension: "year", label: "Año lectivo" },
  { key: "course", dimension: "course", label: "Curso" },
  { key: "subject", dimension: "subject", label: "Materia" },
];
const dimensions = [
  ["locality", "Localidad"], ["department", "Departamento"], ["course", "Curso"], ["subject", "Materia"], ["year", "Año lectivo"],
] as const;

function CatalogFilter({ filter, value, onChange }: {
  filter: (typeof filters)[number];
  value: string;
  onChange: (value: string) => void;
}) {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [catalog, setCatalog] = useState<CatalogPage | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    const timeout = window.setTimeout(() => {
      const params = new URLSearchParams({ dimension: filter.dimension, page: String(page), pageSize: "50" });
      if (search.trim()) params.set("query", search.trim());
      setLoading(true);
      setError(false);
      void callCentral<CatalogPage>(`stats/catalogs?${params.toString()}`)
        .then(result => { if (active) setCatalog(result); })
        .catch(() => { if (active) setError(true); })
        .finally(() => { if (active) setLoading(false); });
    }, 150);
    return () => { active = false; window.clearTimeout(timeout); };
  }, [filter.dimension, page, search]);

  function updateSearch(next: string) {
    setSearch(next);
    setPage(1);
  }

  const selectedVisible = catalog?.items.some(item => item.value === value) ?? false;
  return <div className="grid min-w-0 gap-1.5">
    <label className="text-sm font-medium" htmlFor={`share-search-${filter.key}`}>{filter.label}</label>
    <input id={`share-search-${filter.key}`} type="search" value={search}
      onChange={event => updateSearch(event.target.value)} placeholder={`Buscar ${filter.label.toLocaleLowerCase("es-AR")}`}
      aria-label={`Buscar ${filter.label.toLocaleLowerCase("es-AR")}`} className="rounded-md border bg-background px-3 py-2 text-sm" />
    <select value={value} onChange={event => onChange(event.target.value)}
      aria-label={`Seleccionar ${filter.label.toLocaleLowerCase("es-AR")}`} className="rounded-md border bg-background px-3 py-2 text-sm">
      <option value="">Todos</option>
      {value && !selectedVisible ? <option value={value}>{value}</option> : null}
      {(catalog?.items ?? []).map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
    </select>
    <div className="flex items-center justify-between text-xs text-muted-foreground" aria-live="polite">
      <span>{loading ? "Buscando…" : error ? "Catálogo no disponible" : `${catalog?.totalCount ?? 0} opciones`}</span>
      <span className="flex items-center gap-2">
        <button type="button" onClick={() => setPage(current => Math.max(1, current - 1))} disabled={page <= 1 || loading} aria-label={`Página anterior de ${filter.label}`}>‹</button>
        <span>{page}</span>
        <button type="button" onClick={() => setPage(current => current + 1)} disabled={loading || !catalog || page * catalog.pageSize >= catalog.totalCount} aria-label={`Página siguiente de ${filter.label}`}>›</button>
      </span>
    </div>
  </div>;
}

export function ShareStatsForm() {
  const [groupBy, setGroupBy] = useState<(typeof dimensions)[number][0]>("locality");
  const [filterValues, setFilterValues] = useState<Record<FilterKey, string>>({ departmentId: "", localityId: "", schoolYear: "", course: "", subject: "" });
  const [created, setCreated] = useState<StatsShareCreated | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  async function create() {
    setBusy(true);
    setMessage("");
    const selectedFilters = Object.fromEntries(Object.entries(filterValues).filter(([, value]) => value)) as Record<string, string>;
    try { setCreated(await createStatsShare({ groupBy, filters: selectedFilters, metrics: ["attemptCount", "weightedScorePercent"] })); }
    catch (error) { setMessage(error instanceof Error ? error.message : "No se pudo crear el enlace."); }
    finally { setBusy(false); }
  }

  async function revoke() {
    if (!created) return;
    setBusy(true);
    setMessage("");
    try { await revokeStatsShare(created.id); setCreated(null); setMessage("El enlace fue revocado."); }
    catch (error) { setMessage(error instanceof Error ? error.message : "No se pudo revocar el enlace."); }
    finally { setBusy(false); }
  }

  return <section className="mt-6 max-w-4xl rounded-lg border bg-card p-5">
    <label className="grid gap-2 text-sm font-medium" htmlFor="share-group-by">Agrupar los resultados por</label>
    <select id="share-group-by" className="mt-2 w-full rounded-md border bg-background px-3 py-2" value={groupBy} onChange={event => setGroupBy(event.target.value as typeof groupBy)} disabled={busy || created !== null}>
      {dimensions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
    </select>
    <fieldset className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <legend className="mb-2 text-sm font-medium">Filtros opcionales</legend>
      {filters.map(filter => <CatalogFilter key={filter.key} filter={filter} value={filterValues[filter.key]}
        onChange={value => setFilterValues(previous => ({ ...previous, [filter.key]: value }))} />)}
    </fieldset>
    <p className="mt-3 text-sm text-muted-foreground">El enlace guarda una copia fija de los agregados, vence en 7 días y aplica supresión para cohortes menores a 5.</p>
    <button className="mt-4 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground disabled:opacity-60" disabled={busy || created !== null} onClick={create} type="button">{busy ? "Procesando…" : "Crear enlace"}</button>
    {created && <div className="mt-4 grid gap-2 rounded-md border p-3">
      <label className="text-sm font-medium" htmlFor="created-share-url">Enlace público</label>
      <input id="created-share-url" className="w-full rounded-md border bg-background px-3 py-2 text-sm" readOnly value={typeof window === "undefined" ? created.url : new URL(created.url, window.location.origin).toString()} onFocus={event => event.currentTarget.select()} />
      <p className="text-xs text-muted-foreground">Vence: {new Date(created.expiresAt).toLocaleString()}</p>
      <div className="flex gap-3 text-sm"><Link className="underline" href={created.url}>Abrir</Link><button className="underline" disabled={busy} onClick={revoke} type="button">Revocar</button></div>
      <p className="text-xs text-muted-foreground">Guardá este enlace ahora: el token se muestra una sola vez.</p>
    </div>}
    {message && <p role="status" className="mt-3 text-sm">{message}</p>}
  </section>;
}
