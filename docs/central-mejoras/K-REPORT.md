# Informe de implementación K — durabilidad Local→Central

Fecha: 2 de octubre de 2026. Se implementaron cambios de transporte y contratos de sync, sin desplegar ni modificar datos de producción. Se leyeron `EXTENSION-K-PLAN.md` y `K-PLAN-REPORT.md` del worktree de planificación antes de editar; no se cambiaron pantallas de alumno H, el hook I/J, ni la UI operativa Local M, y no se tocaron las estadísticas Central existentes.

## Causas confirmadas en el código

- `SyncBackgroundService` calculaba la actividad reciente de sesiones y saltaba el bloque completo de sync mientras una sesión estaba activa. Ese bloque también era el único que llamaba `LocalOutboxPushService.PushAsync`; por ello una respuesta enviada durante una sesión podía quedar en SQLite hasta cerrar o volver obsoleta la sesión. `SyncEndpoints` además documentaba el envío como manual, aunque el worker intentaba hacerlo sólo en ticks idle.
- El heartbeat tenía ruta, cliente autenticado y endpoint Central reales: `POST /api/sync/session-heartbeat` y `SessionHeartbeatController.Receive`. Central persistía `DeliverySessions.LastHeartbeatAt` y respondía `receivedAt`. Local descartaba la respuesta HTTP, incluido su status y cuerpo; un 401/403 o 5xx quedaba indistinguible de un envío satisfactorio. No hay evidencia de una única falla runtime de URL, token o scope que explique todos los casos «sin señal»; no se probó contra producción.
- La política de idempotencia existía, pero el checksum JSON dependía del orden de propiedades. PostgreSQL guarda el inbox como `jsonb` y reordena esas propiedades; el test con PostgreSQL confirmó que un retry del mismo payload podía verse como carga distinta. El checksum nuevo ordena propiedades de objetos y Central sigue aceptando el formato anterior mientras se vacían outbox SQLite creados por versiones previas. Local genera una sola clave y carga dentro de la transacción de intento/outbox, y conserva la fila al fallar el transporte.

## Cambios

- El worker Local sigue mandando heartbeats aparte y ahora empuja el outbox durante una sesión activa; la actividad estudiantil sigue aplazando los pulls de exámenes.
- Local conserva la respuesta pendiente ante offline, timeout o error HTTP y la reintenta con el mismo payload/clave y backoff existente. `accepted` y `duplicate` confirman el outbox sólo después de una respuesta Central que representa recepción durable.
- El heartbeat ahora registra intento, hora enviada, hora Central reconocida, status HTTP y códigos de error acotados en SQLite. No guarda URL, token, respuestas ni texto arbitrario del servidor.
- Central inserta primero en el `sync.inbox` existente y confirma esa transacción antes de procesar el intento. Grading, atribución y rollup se procesan después en una transacción separada. Si esa fase falla, el mismo inbox queda `processing_failed`, conserva el contador y la fecha del próximo intento con backoff de 30 s a 1 h; un índice permite al hosted worker reprocesar filas debidas cada 30 s sin que las fallas antiguas bloqueen las siguientes. Un retry de la misma clave también lo dispara inmediatamente. El lock advisory ya usado para rollups serializa el procesamiento; no se creó una segunda cola ni se cambió la fórmula de rollup. El estado `processed` sólo se guarda junto al commit de attempt/result/rollup. Una migración aditiva agrega los campos y el índice al inbox existente.
- `PushItemResult` agrega `receivedAt` (timestamp de commit Central) y `processingStatus`. Los estados `accepted`/`duplicate` son ACK de recepción durable incluso si el estado posterior es `processing_failed`; el retry conserva la fecha original. Un payload inválido no se guarda como recepción, y una clave con carga distinta sigue visible como conflicto.
- El checksum estable compara contenido sin depender del orden de las claves de objetos JSON; Central acepta además checksums previos para no invalidar filas locales pendientes durante la actualización.
- `GET /api/sync/status` añade `lastHeartbeatAttemptAt`, `lastHeartbeatSentAt`, `lastHeartbeatReceivedAt`, `heartbeatLastHttpStatus`, `heartbeatLastErrorCode`, `lastPushAckAt` (hora Local del ACK) y `lastPushReceivedAt` (hora Central).
- `GET /api/admin/sync/received` expone conteos `inboxProcessing.pending/failed`, la próxima fecha de retry, receipt status/fecha, intentos de procesamiento, grading, atribución y estado de rollup para los registros disponibles. Esto es contrato API para L/M, no una nueva pantalla.

## Verificación

- `PlanCope.Local.Api.Tests`: las 179 pruebas pasaron. Incluyen SQLite temporal con envío durante una sesión activa y el caso de commit Central simulado seguido de ACK perdido, timeout transitorio, reinicio/reintento duplicado y persistencia del error hasta vaciar el outbox.
- `PlanCope.Central.Api.Tests`: 329 pruebas pasaron y 7 pruebas con dependencia PostgreSQL/Docker se omitieron localmente. Incluyen inyección de falla de rollup después de la recepción durable, retry con un contexto nuevo, idempotencia con claves reordenadas y conflicto por carga distinta.
- Los proyectos Central y Local compilaron sin warnings ni errores durante esas ejecuciones.
- Se agregó `Failed_rollup_commit_leaves_no_partial_receipt_and_same_key_retry_converges_once` contra PostgreSQL/Testcontainers, junto con una prueba PostgreSQL de recuperación de pushes concurrentes. Ambas quedaron omitidas localmente por `DockerFact`: este entorno no tiene daemon Docker (`/var/run/docker.sock` ausente). CI encontró y permitió corregir el orden de claves de `jsonb`, el disparo de fallas sintéticas y una pérdida de precisión submicrosegundo al releer timestamps. En la ejecución CI más reciente, Central API (incluidas las pruebas PostgreSQL), migraciones, contenedores y seguridad pasan; el build y tests .NET Local también pasan. La instalación y verificación del cliente web Local siguen corriendo. No se usó base de producción.
- `git diff --check` pasó. No se ejecutó despliegue manual ni automatizado desde este lote.

## Contrato de estado para L/M

- Central heartbeat: `LiveSessionSummary.LastHeartbeatAt` sigue siendo la hora recibida en Central. `null` o una hora anterior a `SessionHeartbeatPolicy.StaleAfter` no representa conexión fresca.
- Local heartbeat: sólo `lastHeartbeatReceivedAt` prueba un ACK parseable 2xx; un status HTTP/error sin esa señal queda como fallo o desconocido.
- Local resultados: `lastPushAckAt` marca el ACK que permitió cerrar una fila local; `lastPushReceivedAt` es la recepción durable informada por Central. `pendingItems` puede ser cero mientras `processingStatus` Central todavía sea `received`/`processing_failed`.
- Central resultado: `receiptStatus`/`durableReceivedAt` describen el inbox; `processingStatus`, `gradingStatus`, `attributionStatus` y `rollupStatus` describen la fase posterior. `inboxProcessing.failed` incluye inbox durables pendientes de reproceso aunque todavía no tengan un intento derivado; `nextRetryAt` da la fecha mínima programada.

## Límites y riesgo pendiente

El cambio confirma causas en el flujo local y agrega pruebas reproducibles, pero no atribuye una instalación real a fallo de credencial, URL, scope o red porque no se accedió a producción. La prueba contra PostgreSQL no se pudo ejecutar localmente por falta de Docker; es el principal pendiente de verificación. La revisión independiente previa a merge y el PR quedan como gates, y requieren resolverse antes de integrar.
