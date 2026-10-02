# Batch M — operación diaria Local

## Entregado

- El inicio consume `lastHeartbeatAttemptAt`, `lastHeartbeatSentAt`, `lastHeartbeatReceivedAt`, `heartbeatLastHttpStatus`, `heartbeatErrorCode` y `lastPushAckAt` del contrato acordado de K. También muestra el último pull, sesiones abiertas/finalizadas e intentos pendientes; `pendingItems` cuenta el outbox local.
- Heartbeat recibido y ACK durable de resultados se muestran por separado. No se infiere frescura porque el contrato aún no aporta una política de antigüedad; los campos ausentes siguen como **Sin dato**. No se atribuyen ACKs a grading o rollup.
- La descarga de activación muestra etapas verificables y el progreso real de listas. Cuando el porcentaje de una descarga de actualización no está disponible, queda indeterminado en vez de mostrar 0 %.
- Estadísticas conserva filtros de año, grado, sección y examen; se retiró la búsqueda libre redundante. Se mantuvo el informe HTML y la selección de examen para crear sesiones.
- Se agregó retorno visible a sesiones con ubicación de pantalla y restauración del foco al título. Los filtros, botones e inputs se alinean y reordenan en móvil.
- Se eliminó la acción CSV, su método del cliente y la ruta Local. La búsqueda en `src/Local` ya no encuentra referencias ejecutables a esa exportación; una prueba de API verifica que la ruta ya no responda.

## Verificación

- Pruebas enfocadas de UI: 54 aprobadas en seis archivos.
- Build de ClientApp: `tsc` y Vite aprobados.
- Pruebas API enfocadas de activación y ruta retirada: 4 aprobadas.
- Vista previa Chromium con fixture local simulado: inicio a 1440×900 y 390×844; Estadísticas a 390×844. `scrollWidth` coincidió con el ancho de viewport en los tres casos.
- Teclado: se abrió Estadísticas con Enter, se volvió a sesiones con Enter y el foco quedó en el título «Sesiones abiertas».

## Integración con K

El PR de K incorporará esos campos a la respuesta Local; hasta que llegue a la versión del backend, la UI los muestra como **Sin dato**. Los estados de procesamiento posterior al ACK durable aún no están en el contrato confirmado. No se añaden métricas ni umbrales de frescura.

Revisión independiente y CI continúan pendientes; no fusionar este PR antes de ambos.

El PR queda abierto a revisión independiente y CI; este lote no se fusiona ni despliega.

## Entrega

- Base: `origin/main` (`b34f2d9`).
- Commits: `ec0a826` (`feat(local): improve M operator operations`), `059f6e8` (`feat(local): consume K heartbeat and ack status`).
- PR en borrador: [#108](https://github.com/GeronimoSerial/plan_cope/pull/108).
- CI al cierre: `local-app` y `security` pendientes; `changes` y GitGuardian aprobados. Revisión independiente pendiente.
