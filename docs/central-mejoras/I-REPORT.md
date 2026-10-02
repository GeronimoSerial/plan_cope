# Batch I — recuperación de examen Local

## Implementación

- Al iniciar un intento nominal o anónimo, el navegador genera una credencial aleatoria de 256 bits y la conserva en `sessionStorage` antes de llamar a la API. El servidor persiste únicamente su SHA-256, ligado al intento y la sesión, con vencimiento de cuatro horas.
- El mismo secreto permite recuperar intento, bloques y respuestas; también es obligatorio en las rutas existentes de respuestas y entrega. La ruta de reanudación por sesión permite recuperar un inicio cuya respuesta HTTP se perdió, usando la credencial temporal ya guardada. Un reintento del mismo inicio no crea un segundo intento. Si una credencial vence, el flujo nominal permite verificar de nuevo el DNI y recuperar el intento abierto con una credencial nueva.
- SQLite agrega la tabla `attempt_resume_credentials` y la revisión por respuesta mediante la migración aditiva `020_AttemptResumeAndAnswerRevisions.sql`. Las respuestas con revisiones repetidas o atrasadas se reconcilian idempotentemente; la transacción rechaza escrituras cuando la entrega ya cerró el intento.
- El gate compartido serializa el guardado autenticado con la lectura de respuestas, generación del payload y transacción de entrega/outbox. La entrega desde cierre docente y la finalización en segundo plano pasan por el mismo servicio; los eventos siguen siendo idempotentes porque el cambio de estado y la inserción del outbox se confirman en una única transacción.
- La compilación también corrige el nombre local duplicado `usedAt` y conserva HTTP 425 (`Too Early`) para la respuesta `resume_pending`.
- El hook guarda cambios mínimos por bloque en `sessionStorage` antes del debounce, confirma cada revisión en orden y sólo entonces la quita del buffer. Reintenta al volver la conexión; la entrega vacía la cola primero y bloquea edición durante el envío. El estado distingue guardando, guardada y pendiente.
- Al restaurar, el hook combina las respuestas del servidor con las revisiones pendientes de `sessionStorage` y las muestra antes de reintentar el guardado. Así el examen vuelve a estar disponible aunque falle la conexión; el debounce y el evento `online` confirman después las revisiones pendientes.
- El flujo permite iniciar sesiones anónimas sin DNI; las sesiones nominales mantienen resolución y confirmación de identidad. Entrega, cierre observado y credencial vencida/inválida limpian la credencial y el buffer.

## Pruebas añadidas o ajustadas

- `LocalSessionFlowTests`: restore protegido, rechazo de restore/answers/submit con sólo `attemptId`, reintento idempotente nominal/anónimo, orden/repetición de revisiones, reingreso nominal tras expirar la credencial, carrera autosave-entrega y ocho carreras de guardado/cierre docente que comprueban estado enviado, payload confirmado en outbox y un único evento.
- `AttemptGradingTests` y flujos existentes: envían credencial/revisión y esperan rechazo autenticado tras revocación por entrega o cierre.
- `studentApi.test.ts`: credencial en inicio, descubrimiento tras respuesta perdida, restore autenticado y payload versionado de autosave.
- `useStudentExam.resume.test.tsx`: muestra la respuesta pendiente del buffer tras restaurar sin conexión, conserva el intento y la revisión, y la confirma al recuperar la conexión. `studentApi.test.ts` también comprueba el bearer obligatorio en la entrega.
- `ExamTakingPanel.virtualize.test.tsx`: comprueba que el editor se bloquea durante el envío y que el estado pendiente se presenta como advertencia.
- `SyncBackgroundServiceTests`: reemplaza esperas fijas de 17 segundos por espera acotada del estado durable esperado (fila enviada y error/origen correctos), evitando detener el servicio antes de terminar el segundo ciclo de reintento.

## Diagnóstico de CI y revisión funcional

- En PR #112, CI run `37014334125`, el build Local con warnings-as-errors pasó y falló `SyncBackgroundServiceTests.Successful_outbox_retry_clears_push_error_while_exam_pull_is_gated` en la línea 329: esperaba error vacío y leyó `Central push failed: 401.` (184/185 API tests aprobadas). El test esperaba 17 segundos aunque el ciclo de sesión activa vuelve a intentar cada 15; bajo carga el test podía cerrar el servicio durante el procesamiento durable del segundo push. La regresión ahora espera el estado de salida completo y conserva las aserciones sobre push, outbox y error.
- Con API Local en puerto local y una base SQLite temporal aislada, se inició un examen anónimo y otro nominal de fixture. En ambos, refrescar la misma pestaña restauró respuesta y alumno con un intento por sesión; la vista nominal no pidió nuevamente el DNI. Una edición anónima mientras el navegador estaba offline quedó en el buffer con estado pendiente; al volver online, se guardó y el buffer se vació.
- En la sesión nominal, la confirmación de entrega envió el bearer de reanudación, recibió la pantalla de entrega registrada y limpió el estado de la pestaña. La sesión de fixture quedó `submitted` con un único intento.
- Capturas de Chrome: [anónimo pendiente offline](I-REPORT/captures/anonymous-offline-pending-1440x900.png) y [anónimo restaurado](I-REPORT/captures/anonymous-restored-1440x900.png) a 1440 × 900; [nominal restaurado](I-REPORT/captures/nominal-restored-780x500.png) a 780 × 493; [entrega confirmada](I-REPORT/captures/nominal-submit-confirmation-1440x900.png) a 1440 × 900.

## Verificación y límites

- `/home/gero/.dotnet/dotnet build tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --no-restore --configuration Release -warnaserror` — 0 warnings, 0 errores.
- `/home/gero/.dotnet/dotnet test tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --no-build --no-restore --configuration Release` — suite completa: 185 aprobadas, 0 fallidas.
- `/home/gero/.dotnet/dotnet build src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj --no-restore --configuration Release -warnaserror -p:EnableWindowsTargeting=true` — build Windows Host y bundle completados, 0 warnings, 0 errores.
- `dotnet test tests/PlanCope.Local.Host.Tests/PlanCope.Local.Host.Tests.csproj --configuration Release` — 57 aprobadas; `dotnet test tests/PlanCope.E2E.Tests/PlanCope.E2E.Tests.csproj --configuration Release` — 7 aprobadas.
- `npm run test --workspace plancope-local-host-ui` — 25 archivos, 140 pruebas aprobadas. `npm run build --workspace plancope-local-host-ui` pasó; `npm run check:bundle --workspace plancope-local-host-ui` midió 322.6 KiB frente al límite de 341.8 KiB.
- La regresión `Concurrent_answer_save_and_teacher_close_preserve_every_confirmed_answer_in_durable_outbox` pasó en ejecución focalizada y dentro de la suite completa; ejercita ocho intentos concurrentes y confirma que cada respuesta con HTTP 204 figura en el payload durable y que hay exactamente un evento outbox por intento.
- `git diff --check` pasó.
- No se probó contra producción. No apareció un archivo `I-PLAN.md` separado en este checkout; se usaron el alcance de PR #112 y este reporte.
