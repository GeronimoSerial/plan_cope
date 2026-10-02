# Batch I — recuperación de examen Local

## Implementación

- Al iniciar un intento nominal o anónimo, el navegador genera una credencial aleatoria de 256 bits y la conserva en `sessionStorage` antes de llamar a la API. El servidor persiste únicamente su SHA-256, ligado al intento y la sesión, con vencimiento de cuatro horas.
- El mismo secreto permite recuperar intento, bloques y respuestas; también es obligatorio en las rutas existentes de respuestas y entrega. La ruta de reanudación por sesión permite recuperar un inicio cuya respuesta HTTP se perdió, usando la credencial temporal ya guardada. Un reintento del mismo inicio no crea un segundo intento. Si una credencial vence, el flujo nominal permite verificar de nuevo el DNI y recuperar el intento abierto con una credencial nueva.
- SQLite agrega la tabla `attempt_resume_credentials` y la revisión por respuesta mediante la migración aditiva `020_AttemptResumeAndAnswerRevisions.sql`. Las respuestas con revisiones repetidas o atrasadas se reconcilian idempotentemente; la transacción rechaza escrituras cuando la entrega ya cerró el intento.
- El hook guarda cambios mínimos por bloque en `sessionStorage` antes del debounce, confirma cada revisión en orden y sólo entonces la quita del buffer. Reintenta al volver la conexión; la entrega vacía la cola primero y bloquea edición durante el envío. El estado distingue guardando, guardada y pendiente.
- El flujo permite iniciar sesiones anónimas sin DNI; las sesiones nominales mantienen resolución y confirmación de identidad. Entrega, cierre observado y credencial vencida/inválida limpian la credencial y el buffer.

## Pruebas añadidas o ajustadas

- `LocalSessionFlowTests`: restore protegido, rechazo de restore/answers/submit con sólo `attemptId`, reintento idempotente nominal/anónimo, orden/repetición de revisiones, reingreso nominal tras expirar la credencial y carrera autosave-entrega.
- `AttemptGradingTests` y flujos existentes: envían credencial/revisión y esperan rechazo autenticado tras revocación por entrega o cierre.
- `studentApi.test.ts`: credencial en inicio, descubrimiento tras respuesta perdida, restore autenticado y payload versionado de autosave.
- `ExamTakingPanel.virtualize.test.tsx`: comprueba que el editor se bloquea durante el envío y que el estado pendiente se presenta como advertencia.

## Verificación y límites

- `git diff --check` pasó.
- No se pudieron ejecutar `dotnet test` ni las pruebas/build de Vite: este worktree no tiene `dotnet`, `node` ni `npm` en `PATH`. Por eso no se afirma que la suite compiló o pasó; la revisión independiente debe ejecutar las pruebas en un entorno con esos runtimes.
- No se hizo una verificación visual en navegador. Los escenarios nominal/anónimo, offline/online y carrera autosave/entrega están implementados en el flujo, pero requieren validación de integración con los runtimes disponibles.
