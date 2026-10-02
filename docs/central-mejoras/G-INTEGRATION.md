# Integración del lote G — Compartir estadísticas

## Base y alcance

Integración hecha en un worktree Orca nuevo desde `origin/main` actualizado (`0777f7661a406d25a3bcf9ce2e4ca5b08fc7d168`), donde ya está integrado A–F (PR #104). Se portó sólo el lote G y sus pruebas; no se copiaron los cambios parciales de H–J ni reemplazos de D anteriores a A–F. El worktree fuente `central-mejoras` quedó intacto.

## Resultado de la revisión independiente

La implementación conserva sólo un hash SHA-256 del token aleatorio de 256 bits; lo entrega en el cuerpo de creación, sin repetirlo en `Location`, y no lo incluye en eventos de auditoría. Los enlaces vencidos, revocados, desconocidos y malformados responden 404. La página y API públicas responden sin sesión, `no-store`, `no-referrer` y `noindex`; el BFF público no reenvía cookies, credenciales ni filtros.

La revisión encontró que las etiquetas originales de filas con cohortes menores a cinco revelaban qué grupos estaban suprimidos. Ahora las filas pequeñas se combinan en una sola fila genérica y se suprimen tanto sus valores como los totales complementarios. El snapshot conserva filtros y métricas agregados fijos, agrupa en SQL, limita a 5.001 resultados para rechazar más de 5.000 grupos y no serializa CUE, escuela, versión, estudiante, respuesta, intento ni sesión.

La API de escritura exige Admin y aplica `roster_scope`/`roster_cue`; falta de scope explícito se rechaza, un scope escolar sólo crea sobre sus CUE y sólo el creador puede revocar. La UI Admin ofrece agrupación y catálogos buscables/paginados para departamento, localidad, año, curso y materia. La vista anónima deja la tabla siempre visible y carga el gráfico al pedirlo.

## Verificación

- `npm ci --workspace plancope-central-web`: correcto con el lock raíz; 733 paquetes auditados, 0 vulnerabilidades.
- `npm run test --workspace plancope-central-web`: 262 pruebas pasan.
- `npm run build --workspace plancope-central-web -- --webpack`: correcto; incluye `/estadisticas/compartir`, `/estadisticas/compartidas/[token]` y `/api/public/stats/[token]`.
- `dotnet build src/Central/PlanCope.Central.Api/PlanCope.Central.Api.csproj`: correcto, 0 warnings y 0 errores.
- Pruebas API focalizadas: 24 pasan; la prueba PostgreSQL de upgrade está presente pero omitida por falta del daemon Docker.
- `dotnet ef migrations has-pending-model-changes`: “No changes have been made to the model since the last migration.”
- `dotnet ef migrations script 20261002000600_AddLiveSessionHeartbeats 20261002010112_AddStatsShares`: generado en `/tmp/central-stats-share.sql`; crea `stats.stats_shares` y los índices esperados.
- Se intentó el fixture PostgreSQL aislado con Docker/Testcontainers. No pudo ejecutarse: `docker info` falló porque no existe `/var/run/docker.sock`; tampoco hay servidor PostgreSQL local. No se aplicó la migración a una base viva.
- Revisión headless con Chrome a 1440×1000: la URL pública cargó sin redirección al login y emitió `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer` y `X-Robots-Tag: noindex, nofollow`. El backend no estaba disponible durante esa captura, así que se verificó el estado de error anónimo, no una muestra con datos.

## Refs

- PR: [#106](https://github.com/GeronimoSerial/plan_cope/pull/106), commit `09ca0d7`.
- Merge: en espera de checks CI y protección de `main`; no se usó bypass.
- No se desplegó ni publicó una release.
