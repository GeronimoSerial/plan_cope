# Batch I — recuperación de examen Local

## Implementación

- Al iniciar un intento nominal o anónimo, el navegador genera una credencial aleatoria de 256 bits y la conserva en `sessionStorage` antes de llamar a la API. El servidor persiste únicamente su SHA-256, ligado al intento y la sesión, con vencimiento de cuatro horas.
- El mismo secreto permite recuperar intento, bloques y respuestas; también es obligatorio en las rutas existentes de respuestas y entrega. La ruta de reanudación por sesión permite recuperar un inicio cuya respuesta HTTP se perdió, usando la credencial temporal ya guardada. Un reintento del mismo inicio no crea un segundo intento. Si una credencial vence, el flujo nominal permite verificar de nuevo el DNI y recuperar el intento abierto con una credencial nueva.
- SQLite agrega la tabla `attempt_resume_credentials` y la revisión por respuesta mediante la migración aditiva `020_AttemptResumeAndAnswerRevisions.sql`. Las respuestas con revisiones repetidas o atrasadas se reconcilian idempotentemente; la transacción rechaza escrituras cuando la entrega ya cerró el intento.
- El gate compartido serializa el guardado autenticado con la lectura de respuestas, generación del payload y transacción de entrega/outbox. La entrega desde cierre docente y la finalización en segundo plano pasan por el mismo servicio; los eventos siguen siendo idempotentes porque el cambio de estado y la inserción del outbox se confirman en una única transacción.
- La compilación también corrige el nombre local duplicado `usedAt` y conserva HTTP 425 (`Too Early`) para la respuesta `resume_pending`.
- El hook guarda cambios mínimos por bloque en `sessionStorage` antes del debounce, confirma cada revisión en orden y sólo entonces la quita del buffer. Reintenta al volver la conexión; la entrega vacía la cola primero y bloquea edición durante el envío. El estado distingue guardando, guardada y pendiente.
- El flujo permite iniciar sesiones anónimas sin DNI; las sesiones nominales mantienen resolución y confirmación de identidad. Entrega, cierre observado y credencial vencida/inválida limpian la credencial y el buffer.

## Pruebas añadidas o ajustadas

- `LocalSessionFlowTests`: restore protegido, rechazo de restore/answers/submit con sólo `attemptId`, reintento idempotente nominal/anónimo, orden/repetición de revisiones, reingreso nominal tras expirar la credencial, carrera autosave-entrega y ocho carreras de guardado/cierre docente que comprueban estado enviado, payload confirmado en outbox y un único evento.
- `AttemptGradingTests` y flujos existentes: envían credencial/revisión y esperan rechazo autenticado tras revocación por entrega o cierre.
- `studentApi.test.ts`: credencial en inicio, descubrimiento tras respuesta perdida, restore autenticado y payload versionado de autosave.
- `ExamTakingPanel.virtualize.test.tsx`: comprueba que el editor se bloquea durante el envío y que el estado pendiente se presenta como advertencia.

## Verificación y límites

- `/home/gero/.dotnet/dotnet test tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --no-restore` — compilación y suite completas: 185 aprobadas, 0 fallidas.
- La regresión `Concurrent_answer_save_and_teacher_close_preserve_every_confirmed_answer_in_durable_outbox` pasó en ejecución focalizada y dentro de la suite completa; ejercita ocho intentos concurrentes y confirma que cada respuesta con HTTP 204 figura en el payload durable y que hay exactamente un evento outbox por intento.
- `git diff --check` pasó.
- No se hizo una verificación visual en navegador. Los escenarios offline/online y refresh en la misma pestaña requieren validación visual/integral adicional; no se probaron contra producción.
