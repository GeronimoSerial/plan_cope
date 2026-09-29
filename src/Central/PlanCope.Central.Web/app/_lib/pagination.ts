// Server-side pagination/filtering helpers for read-only listings (escuelas).
// Pure functions only: safe to unit test under the node vitest environment.

export const PAGE_SIZE = 25;

export type RawSearchParams = Record<string, string | string[] | undefined> | undefined;

export interface PageParams {
  q: string;
  page: number;
  pageSize: number;
}

export interface Paginated<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
  /** 1-based index of the first rendered row (0 when there are no rows). */
  from: number;
  /** 1-based index of the last rendered row (0 when there are no rows). */
  to: number;
}

export type PageWindowItem = number | "ellipsis";

function firstValue(value: string | string[] | undefined): string {
  if (Array.isArray(value)) {
    return value[0] ?? "";
  }
  return value ?? "";
}

/**
 * Reads `q` and `page` from a URLSearchParams-shaped object. The page size is fixed
 * (25) so a huge padrón never renders more than one page worth of rows.
 */
export function parsePageParams(searchParams: RawSearchParams, defaultPageSize: number = PAGE_SIZE): PageParams {
  const q = firstValue(searchParams?.q).trim();
  const parsedPage = Number.parseInt(firstValue(searchParams?.page), 10);
  const page = Number.isFinite(parsedPage) && parsedPage >= 1 ? parsedPage : 1;
  const pageSize = Number.isFinite(defaultPageSize) && defaultPageSize > 0 ? Math.floor(defaultPageSize) : PAGE_SIZE;
  return { q, page, pageSize };
}

/** Lowercase + strip combining diacritics so "Nº" / accents match a plain query. */
export function normalizeText(value: string | null | undefined): string {
  if (!value) {
    return "";
  }
  return value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .trim();
}

/** True when any field contains the query (case/accent-insensitive). Empty query matches all. */
export function matchesQuery(
  query: string,
  fields: readonly (string | number | null | undefined)[]
): boolean {
  const normalized = normalizeText(query);
  if (normalized === "") {
    return true;
  }
  return fields.some(field => normalizeText(field === null || field === undefined ? "" : String(field)).includes(normalized));
}

/** Filters by a case/accent-insensitive `contains` across the selected fields. */
export function filterByQuery<T>(
  items: readonly T[],
  query: string,
  fields: (item: T) => readonly (string | number | null | undefined)[]
): T[] {
  if (normalizeText(query) === "") {
    return items.slice();
  }
  return items.filter(item => matchesQuery(query, fields(item)));
}

/** Slices one page, clamping the requested page to the available range. */
export function paginate<T>(items: readonly T[], page: number, pageSize: number): Paginated<T> {
  const total = items.length;
  const safePageSize = Number.isFinite(pageSize) && pageSize > 0 ? Math.floor(pageSize) : PAGE_SIZE;
  const totalPages = Math.max(1, Math.ceil(total / safePageSize));
  const requested = Number.isFinite(page) ? Math.floor(page) : 1;
  const safePage = Math.min(Math.max(requested, 1), totalPages);
  const start = (safePage - 1) * safePageSize;
  const slice = items.slice(start, start + safePageSize);
  const from = total === 0 ? 0 : start + 1;
  const to = total === 0 ? 0 : start + slice.length;
  return { items: slice, page: safePage, pageSize: safePageSize, total, totalPages, from, to };
}

/** "Mostrando X–Y de N", used next to the pagination controls. */
export function formatShowing(from: number, to: number, total: number): string {
  if (total === 0) {
    return "Sin resultados";
  }
  return `Mostrando ${from}–${to} de ${total}`;
}

/**
 * Page numbers to render around the current page, plus "ellipsis" markers where
 * the sequence is broken. Always keeps the first and last page reachable.
 */
export function buildPageWindow(current: number, totalPages: number, span: number = 1): PageWindowItem[] {
  if (totalPages <= 0) {
    return [];
  }
  if (totalPages === 1) {
    return [1];
  }
  const pages = new Set<number>([1, totalPages]);
  for (let candidate = current - span; candidate <= current + span; candidate += 1) {
    if (candidate >= 1 && candidate <= totalPages) {
      pages.add(candidate);
    }
  }
  const sorted = [...pages].sort((a, b) => a - b);
  const result: PageWindowItem[] = [];
  let previous = 0;
  for (const page of sorted) {
    if (previous !== 0 && page - previous > 1) {
      result.push("ellipsis");
    }
    result.push(page);
    previous = page;
  }
  return result;
}

/** Builds a listing URL preserving `q` and dropping `page` when it is the first page. */
export function buildPageHref(basePath: string, q: string, page: number): string {
  const params = new URLSearchParams();
  if (q) {
    params.set("q", q);
  }
  if (page > 1) {
    params.set("page", String(page));
  }
  const query = params.toString();
  return query ? `${basePath}?${query}` : basePath;
}
