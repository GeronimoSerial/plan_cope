# Lote B — alta de examen por título

## Cambios

- El formulario de alta pide título y curso, y envía la solicitud sin `code`. La validación de alta conserva la validación de códigos cuando un cliente legacy sí los proporciona.
- El contrato acepta `code` ausente o `null`; la API genera un valor técnico `exam-<GUID>` independiente del título. Un código legacy proporcionado continúa guardándose sin cambios, y siguen vigentes la validación y el control de duplicados.
- Se quitó el código de la edición, búsqueda, tablas de exámenes, inicio, detalle, breadcrumb y formulario del builder. El documento interno y los contratos de publicación conservan el campo para mantener los paquetes existentes.
- Se añadieron pruebas de alta con y sin código, deserialización del contrato sin `code`, validación del esquema y envío desde el diálogo.

## Verificación

- Web, pruebas pertinentes: **33 aprobadas** en cuatro archivos, incluido el diálogo de alta.
- SyncCompat, `ExamsContractTests`: **18 aprobadas**.
- ESLint de los archivos web del lote: **sin errores ni advertencias**.
- Suite web completa: **243 aprobadas y 3 fallidas**. Los tres fallos son de `question-preview.test.tsx`, `exam-preview.test.tsx` y `question-card.test.tsx`, en el trabajo concurrente de preview del Lote C; esperan nombres accesibles que el renderer compartido actual no expone.
- Suite API de exámenes: no pudo compilar debido a errores del cambio concurrente en `StatsQueryController.cs` (`SessionHeartbeatPolicy` no resuelto y referencias a `SchoolDimension.Cue`). Por ello, la nueva prueba API de alta sin código y las pruebas existentes del controlador quedan pendientes de ejecución.

## Límites

Los IDs, versiones, `ExamCode` de paquetes, metadata, checksum y sincronización no se modificaron. La ejecución de la API requiere corregir primero los errores de compilación de estadísticas; los tres tests de preview requieren la revisión del Lote C.
