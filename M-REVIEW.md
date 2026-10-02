# M-REVIEW — PR #108

## Veredicto

No fusionar todavía. La implementación revisada no presenta un bloqueo adicional en los cambios de UI/API examinados, pero la CI requerida debe quedar verde en el nuevo `HEAD` y falta evidencia visual de M que pueda revisarse desde el PR.

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

## Verificación y límites

- En el run `36965824826`, `security` y `changes` aprobaron. `local-app` falló solo por la expectativa CSV indicada; los jobs Central y containers se omitieron por alcance. El check agregado `ci` falló como resultado de `local-app`.
- Tras integrar la corrección, el run `36970072049` sobre la cabeza `7112009893be2ddc88b208484dc6f44211e20886` terminó con `local-app`, `security`, `changes`, el agregador `ci` y GitGuardian aprobados. Central y containers se omitieron por alcance. `mergeStateStatus` quedó `CLEAN`; `reviewDecision` aún no registra una revisión formal de GitHub.
- No pude ejecutar localmente `dotnet test` porque este entorno no tiene `dotnet` instalado. La corrección depende de la CI nueva para demostrar el resultado.
- `M-REPORT.md` afirma una vista Chromium a 1440×900 y 390×844, pero el PR no contiene capturas M ni otros artefactos visuales revisables. No hay evidencia en el árbol que permita reproducir esas vistas; las capturas de H no prueban M y no se usaron como sustituto.

## Condición para cerrar

Revisar los checks de la nueva cabeza de PR #108, exigir todos los checks requeridos verdes y completar una revisión satisfactoria antes de fusionar. Adjuntar capturas M de escritorio y móvil si el gate de revisión requiere evidencia visual reproducible.
