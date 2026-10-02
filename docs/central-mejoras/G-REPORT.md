# Lote G — Compartir estadísticas

Estado: implementado en el worktree, sin commit.

## Cambio

- `StatsShare` guarda hash SHA-256 del token, filtros normalizados, JSONB inmutable del snapshot, agrupación, fechas y actor. La migración nueva `20261002010112_AddStatsShares` crea `stats.stats_shares` e índices de hash único y expiración/revocación; no se editaron migraciones aplicadas.
- `POST /api/admin/stats/shares` y `DELETE /api/admin/stats/shares/{id}` requieren política Admin. Para `roster_scope=school`, generación restringe rollups a CUE asignados y la revocación queda limitada al creador; scope provincial/Admin procesa todos los rollups autorizados. Creación y revocación agregan eventos de auditoría sin token.
- La agrupación pública acepta localidad, departamento, curso, materia o año. Los filtros aceptan sólo departamento/localidad, curso, materia y año. Se rechazan escuela/versión, parámetros mutables públicos y vigencia superior a 30 días; por omisión vence a los 7 días. El token usa 256 bits aleatorios, se entrega en la respuesta de creación y sólo se almacena su hash.
- El snapshot aplica scope y filtros, agrupa y suma en SQL sobre rollups existentes; sólo trae IDs de versión distintos para resolver materia y hasta 5.001 filas ya agregadas para detectar el límite. Usa la fórmula ponderada `100 × ΣScore / ΣScoreMax`; rechaza más de 5.000 grupos con indicación de agregar filtros. Aplica `k=5`; si hay alguna celda pequeña también suprime totales y sus denominadores. `null` conserva estados `suppressed`/`unavailable` y no se convierte a cero.
- El JSON público sólo lleva etiquetas y métricas agregadas. No serializa IDs de grupo, CUE, nombres de escuelas, versiones, IDs personales, respuestas, intentos individuales ni sesiones. GET inválido, ausente, expirado o revocado devuelve 404; la lectura usa sólo el snapshot guardado. La página pública vive en `app/estadisticas/compartidas/[token]/`, fuera de `(app)`, y el proxy deja esa ruta anónima. Su BFF `/api/public/stats/[token]` no agrega credenciales ni permite filtros.
- Estadísticas muestra el enlace Compartir sólo a Admin. La página permite agrupar, fijar año/curso/materia, entregar el enlace una vez y revocarlo. La vista pública presenta tabla semántica siempre disponible y carga `StatsBarChart` bajo demanda.

## Verificación

- `dotnet build src/Central/PlanCope.Central.Api/PlanCope.Central.Api.csproj --no-restore`: correcto, cero warnings/errores.
- `dotnet test tests/PlanCope.Central.Api.Tests/PlanCope.Central.Api.Tests.csproj --no-restore --filter 'FullyQualifiedName~StatsShareServiceTests|FullyQualifiedName~StatsQueryControllerTests|FullyQualifiedName~StatsControllerTests|FullyQualifiedName~MigrationDiscoveryTests'`: 34 pasan, 2 fixtures PostgreSQL omitidas porque requieren Docker.
- `dotnet ef migrations script ... --no-build`: generó el SQL e incluyó la creación de `stats.stats_shares`, sus JSONB e índices. `dotnet ef migrations has-pending-model-changes ... --no-build`: sin cambios entre modelo y snapshot.
- La prueba `SnapshotAggregateQueries_TranslateScopeGeographyAndAllPublicDimensionsToPostgres` confirma la traducción Npgsql de scope, filtros geográficos, agrupación/sumas, paginación límite y las cinco dimensiones.
- Web: pruebas del BFF público, vista pública, formulario Admin y excepción de autenticación del proxy, 14 pasan. `npm run build --workspace plancope-central-web -- --webpack` compila TypeScript y lista `/estadisticas/compartidas/[token]` y `/api/public/stats/[token]` como rutas dinámicas.
- No se aplicó la migración a un PostgreSQL vivo: las pruebas de integración PostgreSQL se omitieron al no estar disponible Docker.
