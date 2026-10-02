# M-REVIEW — PR #108

## Veredicto

La única falla encontrada era una expectativa obsoleta de la ruta CSV retirada; ya está corregida en PR #108. La revisión visual con fixture confirma que los controles revisados caben sin solaparse en 1440×900 y 390×844. El run `36970901511` sobre `27c1362` sigue ejecutando `local-app`; no fusionar hasta que todos los checks requeridos del último `HEAD` terminen verdes.

## Alcance revisado

- Inicio operativo, descarga de activación, estadísticas, retorno y foco, controles móviles y estados de sincronización.
- Retirada de la exportación CSV en el cliente y en el endpoint Local.
- Compatibilidad de la UI con el contrato K pendiente: sus campos son opcionales y los ausentes se presentan como **Sin dato**.

La búsqueda del código fuente no encontró otros clientes ni rutas que dependan de `/api/stats/export.csv`; la ruta eliminada pertenecía a `PlanCope.Local.Api`.

## Hallazgo corregido

**Requerido antes de CI verde:** `StatsEmptyResultTests.Report_exports_reject_school_without_submitted_attempts` todavía solicitaba `/api/stats/export.csv` y esperaba `400 BadRequest`. La eliminación intencional de la ruta devuelve `404 NotFound`, por lo que falló la CI en `local-app / local-app`.

El log del run `36965824826` registró 175 pruebas aprobadas y una fallida, con `Expected: BadRequest` y `Actual: NotFound` en `StatsEmptyResultTests.cs:56`. El build con warnings tratados como errores sí pasó. Se quitó esa expectativa obsoleta y se conservó la aserción del informe HTML; `StatsEndpointsAggregationTests.Csv_export_route_is_not_available_on_local_api` continúa comprobando que la ruta Local responda 404.

La corrección está en el commit `39de29a` (`test(local-api): drop stale CSV report expectation`) de `review/local-operations-m-ci-fix` y se integra en PR #108 mediante el commit de revisión que acompaña este informe.

## Otros ejes

- **Correctitud:** sesiones finalizadas usan el estado `closed` aceptado por el API; contadores fallidos quedan en **Sin dato**.
- **Arquitectura:** el cambio de CSV está acotado al API y cliente Local; no se hallaron referencias ejecutables en otros clientes.
- **Seguridad y rendimiento:** el diff revisado no añade dependencias, consultas de datos sensibles ni trabajo de red de alta frecuencia; la UI consulta el resumen cada 30 segundos.
- **Contrato K:** los campos de heartbeat/ACK son opcionales en el DTO y los valores ausentes no se convierten en éxito ni en una edad inventada.

## Evidencia visual

Las capturas se generaron con Chromium y el fixture local `m-review-fixture.html`, que simula respuestas de API para hacer visibles los estados de sesiones y estadísticas. Las vistas móviles usaron emulación de dispositivo con viewport CSS de 390×844; `innerWidth`, ancho del documento y ancho del contenido dieron 390 px, sin desbordamiento horizontal.

- Inicio operativo, escritorio: [sessions-1440x900.png](M-REVIEW/captures/sessions-1440x900.png)
- Inicio operativo, móvil: [sessions-390x844.png](M-REVIEW/captures/sessions-390x844.png)
- Estadísticas, móvil: [stats-390x844.png](M-REVIEW/captures/stats-390x844.png)

En móvil, las métricas, filtros y acciones se apilan en una columna y permanecen legibles dentro del viewport; la cabecera se reorganiza en dos niveles. Las capturas documentan la presentación con datos simulados, no una prueba de integración contra una API activa.

## Verificación y límites

- En el run `36965824826`, `security` y `changes` aprobaron. `local-app` falló solo por la expectativa CSV indicada; los jobs Central y containers se omitieron por alcance. El check agregado `ci` falló como resultado de `local-app`.
- Tras integrar la corrección, el run `36970072049` sobre la cabeza `7112009893be2ddc88b208484dc6f44211e20886` terminó con `local-app`, `security`, `changes`, el agregador `ci` y GitGuardian aprobados. Después, el informe se actualizó en `27c1362`; en el run `36970901511`, `changes`, `security` y GitGuardian aprobaron y `local-app` aún ejecuta sus pruebas. Central y containers se omitieron por alcance. `mergeStateStatus` permanece `BLOCKED` hasta cerrar el check pendiente; `reviewDecision` aún no registra una revisión formal de GitHub.
- No pude ejecutar localmente `dotnet test` porque este entorno no tiene `dotnet` instalado. La corrección depende de la CI nueva para demostrar el resultado.
- La descarga real y el progreso operativo se verificaron en el diff y en los estados proporcionados por el fixture; no se hizo una descarga contra un servidor remoto durante la revisión.

## Condición para cerrar

Revisar el resultado final de `local-app` para el último `HEAD`. Si los checks requeridos quedan verdes, la corrección del test y la evidencia visual dejan la revisión satisfactoria para fusionar #108. Si un check falla, corregir y esperar el nuevo run antes de fusionar.
