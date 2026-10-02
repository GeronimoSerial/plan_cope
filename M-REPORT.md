# Batch M — operación diaria Local

## Entregado

- El inicio muestra estado observado de Central, último envío de resultados, última descarga de exámenes, sesiones abiertas y finalizadas, e intentos pendientes. El `pendingItems` existente cuenta elementos pendientes del outbox local.
- Heartbeat y recepción en Central quedan como **Sin dato**: `/api/sync/status` no publica esas señales. No se infiere recepción ni frescura a partir del último envío local; falta integrar el contrato de estado de K.
- La descarga de activación muestra etapas verificables y el progreso real de listas. Cuando el porcentaje de una descarga de actualización no está disponible, queda indeterminado en vez de mostrar 0 %.
- Estadísticas conserva filtros de año, grado, sección y examen; se retiró la búsqueda libre redundante. Se mantuvo el informe HTML y la selección de examen para crear sesiones.
- Se agregó retorno visible a sesiones con ubicación de pantalla y restauración del foco al título. Los filtros, botones e inputs se alinean y reordenan en móvil.
- Se eliminó la acción CSV, su método del cliente y la ruta Local. La búsqueda en `src/Local` ya no encuentra referencias ejecutables a esa exportación; una prueba de API verifica que la ruta ya no responda.

## Verificación

- Pruebas enfocadas de UI: 52 aprobadas en seis archivos.
- Build de ClientApp: `tsc` y Vite aprobados.
- Pruebas API enfocadas de activación y ruta retirada: 4 aprobadas.
- Vista previa Chromium con fixture local simulado: inicio a 1440×900 y 390×844; Estadísticas a 390×844. `scrollWidth` coincidió con el ancho de viewport en los tres casos.
- Teclado: se abrió Estadísticas con Enter, se volvió a sesiones con Enter y el foco quedó en el título «Sesiones abiertas».

## Pendiente de K

El inicio todavía no puede mostrar el último heartbeat enviado/recibido ni la recepción durable de resultados en Central. Para integrarlos hacen falta el contrato Local de K (campos, estados, semántica temporal/frescura y conteo de pendientes), que se solicitó al coordinador y está pendiente. La UI mantiene esas señales en «Sin dato» hasta recibirlo.

El PR queda abierto a revisión independiente y CI; este lote no se fusiona ni despliega.
