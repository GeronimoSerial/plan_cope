# Batch I — recuperación de examen Local

## Seguimiento PR114 — plan antes de cambios

- Extender la credencial de reanudación con una prueba opaca independiente, aleatoria por pestaña e intento, persistida sólo como hash y ligada en SQLite a `attemptId` y `deliverySessionId`.
- Añadir una ruta de recuperación anónima que acepte únicamente esa prueba, el intento y el código/sesión; dentro de una transacción comprobará coincidencia de sesión, intento abierto y credencial anterior vencida, y rotará la credencial sin crear otro intento. La prueba opaca nunca autorizará restaurar, guardar o entregar directamente.
- Conservar marcas de expiración y revocación en vez de borrar evidencia de credenciales vencidas; revocar la prueba en reset/logout explícito, submit y cierre. La recuperación rechazará prueba inválida, intento/sesión ajenos, estado revocado/cerrado y solicitudes concurrentes después de la primera rotación.
- Mantener la ruta nominal existente y el gate raw/outbox. Actualizar hook, tipos, API cliente, contratos, migración aditiva, API/UI tests para expiración offline-refresh-recuperación-submit, revocación/cierre, prueba inválida/cruce de intentos y concurrencia.
- Ejecutar suites API y UI enfocadas, build Local, revisar diff/estado y CI disponible; actualizar este informe con resultados, límites y SHA, y publicar el commit en la rama PR114 sin force-push.

## Implementación

- Al iniciar un intento nominal o anónimo, el navegador genera una credencial aleatoria de 256 bits y la conserva en `sessionStorage` antes de llamar a la API. El servidor persiste únicamente su SHA-256, ligado al intento y la sesión, con vencimiento de cuatro horas.
- El mismo secreto permite recuperar intento, bloques y respuestas; también es obligatorio en las rutas existentes de respuestas y entrega. La ruta de reanudación por sesión permite recuperar un inicio cuya respuesta HTTP se perdió, usando la credencial temporal ya guardada. Un reintento del mismo inicio no crea un segundo intento. Si una credencial vence, el flujo nominal verifica de nuevo el DNI y usa una operación de recuperación que solo rota credenciales cuando coincide estudiante, sesión e intento abierto.
- SQLite agrega la tabla `attempt_resume_credentials` y la revisión por respuesta mediante la migración aditiva `020_AttemptResumeAndAnswerRevisions.sql`. Las respuestas con revisiones repetidas o atrasadas se reconcilian idempotentemente; la transacción rechaza escrituras cuando la entrega ya cerró el intento.
- El gate compartido serializa el guardado autenticado con la lectura de respuestas, generación del payload y transacción de entrega/outbox. El alta y la renovación de credenciales también revalidan el intento bajo el gate para que una carrera de entrega no reactive una credencial revocada. La entrega desde cierre docente y la finalización en segundo plano pasan por el mismo servicio; los eventos siguen siendo idempotentes porque el cambio de estado y la inserción del outbox se confirman en una única transacción.
- El fallback raw de finalización también toma el gate antes de leer respuestas y lo conserva hasta confirmar intento y outbox en una transacción. El servicio normal libera el gate antes de llamar al fallback; este adquiere una sola vez y libera en `finally`, sin reentrada ni deadlock.
- La compilación también corrige el nombre local duplicado `usedAt` y conserva HTTP 425 (`Too Early`) para la respuesta `resume_pending`.
- El hook guarda cambios mínimos por bloque en `sessionStorage` antes del debounce, confirma cada revisión en orden y sólo entonces la quita del buffer. Reintenta al volver la conexión; la entrega vacía la cola primero y bloquea edición durante el envío. El estado distingue guardando, guardada y pendiente.
- Al restaurar, el hook combina las respuestas del servidor con las revisiones pendientes de `sessionStorage` y las muestra antes de reintentar el guardado. Así el examen vuelve a estar disponible aunque falle la conexión; el debounce y el evento `online` confirman después las revisiones pendientes.
- El flujo permite iniciar sesiones anónimas sin DNI; las sesiones nominales mantienen resolución y confirmación de identidad. Ante 401 por credencial vencida o inválida, el navegador quita la credencial pero conserva el buffer y solicita reidentificación; una identidad distinta no puede vincularlo. Entrega, cierre observado (410) y la acción explícita de corregir/reiniciar identidad siguen limpiando el buffer.

## Seguimiento PR114 — implementación

- `021_AnonymousAttemptRecovery.sql` agrega `revoked_at` y la tabla aditiva `attempt_resume_proofs`; se conserva la fila de credencial vencida para distinguir expiración de revocación. Se guarda sólo SHA-256 de una prueba aleatoria de 256 bits que el hook mantiene en `sessionStorage` por pestaña junto al buffer.
- La recuperación anónima exige código/sesión, `attemptId`, prueba válida ligada a esa sesión e intento anónimo abierto, estado activo de la sesión y credencial existente vencida sin revocación. Renueva esa credencial dentro de una transacción SQLite; una carrera paralela pierde tras la primera rotación. La prueba por sí sola no permite restaurar, guardar ni entregar.
- Submit, cierre de sesión y reset explícito marcan credencial y prueba revocadas. El camino nominal sigue recuperando con resolución vigente y ahora rechaza renovar credenciales revocadas; el gate de mutación raw/outbox permanece activo.
- Si el 401 pertenece a una sesión anónima, el hook usa código y prueba de pestaña para recuperar el mismo intento sin pedir DNI; en sesión nominal vuelve a pedir identidad. El buffer offline y sus revisiones sobreviven ambos flujos.

## Seguimiento PR114 — pruebas y verificación

- `Anonymous_expired_credential_recovers_only_with_bound_proof_and_revokes_on_submit`: respuesta preservada, intento/sesión ajenos y prueba inválida rechazados, recuperación paralela de una sola ganadora, mismo intento, entrega y revocación.
- `Anonymous_recovery_proof_is_revoked_by_reset_and_session_close` y `Nominal_resolution_requires_confirmation_and_stores_only_identity_snapshot`: reset/cierre y revocación nominal no reactivan el acceso.
- UI: recuperación anónima después de refresh con buffer offline, sin llamada a resolución de identidad, autosave recuperado y entrega del mismo intento. API Local: 188 aprobadas, 0 fallidas; UI Local Host: 146 aprobadas, 0 fallidas.
- `dotnet build tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --no-restore --configuration Release -warnaserror`: 0 warnings, 0 errores. TypeScript `tsc --noEmit` y `vite build` pasaron. `git diff --check` sin errores.
- CI de PR #114 al SHA base `e2cca0c07af10ac6496df729c1622e2df9f414e4`: run `37028041326`, checks de CI y seguridad exitosos antes de subir este cambio; el run del nuevo SHA queda pendiente.
- Límite: el browser debe conservar `sessionStorage` de la pestaña para tener la prueba y el buffer. Un cliente anterior a esta migración que ya perdió su credencial no tiene prueba anónima recuperable; sigue disponible el reingreso nominal con identidad. Reset offline limpia el secreto local y revoca en el host cuando vuelve la conexión.

## Pruebas añadidas o ajustadas

- `LocalSessionFlowTests`: restore protegido, rechazo de restore/answers/submit con sólo `attemptId`, reintento idempotente nominal/anónimo, orden/repetición de revisiones, reingreso nominal tras expirar la credencial, carrera autosave-entrega y ocho carreras de guardado/cierre docente que comprueban estado enviado, payload confirmado en outbox y un único evento.
- `AttemptGradingTests` y flujos existentes: envían credencial/revisión y esperan rechazo autenticado tras revocación por entrega o cierre.
- `studentApi.test.ts`: credencial en inicio, descubrimiento tras respuesta perdida, restore autenticado y payload versionado de autosave.
- `useStudentExam.resume.test.tsx`: muestra la respuesta pendiente del buffer tras restaurar sin conexión, conserva el intento y la revisión, y la confirma al recuperar la conexión. `studentApi.test.ts` también comprueba el bearer obligatorio en la entrega.
- `useStudentExam.resume.test.tsx`: cubre 401 al restaurar y durante un autosave activo; en ambos casos conserva el buffer sin credencial. Tras reidentificación descarga otra vez bloques/respuestas, combina respuestas del servidor con cambios pendientes y luego reintenta el buffer.
- `LocalSessionFlowTests`: verifica recuperación tras expiración con el mismo DNI, rechazo de `attemptId` solo y de otro estudiante, y renovación válida de credencial. Ocho carreras entre autosave y fallback raw confirman que cada respuesta HTTP 204 aparece en el outbox y que solo existe un evento durable.
- `ExamTakingPanel.virtualize.test.tsx`: comprueba que el editor se bloquea durante el envío y que el estado pendiente se presenta como advertencia.
- `SyncBackgroundServiceTests`: reemplaza esperas fijas de 17 segundos por espera acotada del estado durable esperado (fila enviada y error/origen correctos), evitando detener el servicio antes de terminar el segundo ciclo de reintento.

## Diagnóstico de CI y revisión funcional

- En PR #112, CI run `37014334125`, el build Local con warnings-as-errors pasó y falló `SyncBackgroundServiceTests.Successful_outbox_retry_clears_push_error_while_exam_pull_is_gated` en la línea 329: esperaba error vacío y leyó `Central push failed: 401.` (184/185 API tests aprobadas). El test esperaba 17 segundos aunque el ciclo de sesión activa vuelve a intentar cada 15; bajo carga el test podía cerrar el servicio durante el procesamiento durable del segundo push. La regresión ahora espera el estado de salida completo y conserva las aserciones sobre push, outbox y error.
- Con API Local en puerto local y una base SQLite temporal aislada, se inició un examen anónimo y otro nominal de fixture. En ambos, refrescar la misma pestaña restauró respuesta y alumno con un intento por sesión; la vista nominal no pidió nuevamente el DNI. Una edición anónima mientras el navegador estaba offline quedó en el buffer con estado pendiente; al volver online, se guardó y el buffer se vació.
- En la sesión nominal, la confirmación de entrega envió el bearer de reanudación, recibió la pantalla de entrega registrada y limpió el estado de la pestaña. La sesión de fixture quedó `submitted` con un único intento.
- Capturas de Chrome: [anónimo pendiente offline](I-REPORT/captures/anonymous-offline-pending-1440x900.png) y [anónimo restaurado](I-REPORT/captures/anonymous-restored-1440x900.png) a 1440 × 900; [nominal restaurado](I-REPORT/captures/nominal-restored-780x500.png) a 780 × 493; [entrega confirmada](I-REPORT/captures/nominal-submit-confirmation-1440x900.png) a 1440 × 900.

## Verificación y límites

- `/home/gero/.dotnet/dotnet build tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --no-restore --configuration Release -warnaserror` — 0 warnings, 0 errores.
- `/home/gero/.dotnet/dotnet test tests/PlanCope.Local.Api.Tests/PlanCope.Local.Api.Tests.csproj --configuration Release` — suite completa: 186 aprobadas, 0 fallidas (verificación postmerge).
- `/home/gero/.dotnet/dotnet build src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj --no-restore --configuration Release -warnaserror -p:EnableWindowsTargeting=true` — build Windows Host y bundle completados, 0 warnings, 0 errores.
- Harness manual para esta corrección: N/A; la expiración, reidentificación, aislamiento y carreras se ejercitan con integración API SQLite y pruebas UI jsdom. No se accedió a producción ni a una sesión real.
- `dotnet test tests/PlanCope.Local.Host.Tests/PlanCope.Local.Host.Tests.csproj --configuration Release` — 57 aprobadas; `dotnet test tests/PlanCope.E2E.Tests/PlanCope.E2E.Tests.csproj --configuration Release` — 7 aprobadas.
- `npm run test --workspace plancope-local-host-ui` — 25 archivos, 144 pruebas aprobadas (verificación postmerge). `npm run build --workspace plancope-local-host-ui` pasó; `npm run check:bundle --workspace plancope-local-host-ui` midió 324.0 KiB frente al límite de 341.8 KiB.
- `dotnet test tests/PlanCope.SyncCompat.Tests/PlanCope.SyncCompat.Tests.csproj --configuration Release` — 49 aprobadas, 0 fallidas.
- La revisión de diff encontró y cerró una carrera adicional entre la renovación de credencial y la entrega: la respuesta de inicio/reanudación revalida el estado bajo `AttemptMutationGate` antes de persistir credencial y devolver intento/bloques.
- La regresión `Concurrent_answer_save_and_teacher_close_preserve_every_confirmed_answer_in_durable_outbox` pasó en ejecución focalizada y dentro de la suite completa; ejercita ocho intentos concurrentes y confirma que cada respuesta con HTTP 204 figura en el payload durable y que hay exactamente un evento outbox por intento.
- `git diff --check` pasó.
- No se probó contra producción. No apareció un archivo `I-PLAN.md` separado en este checkout; se usaron el alcance de PR #112 y este reporte.
