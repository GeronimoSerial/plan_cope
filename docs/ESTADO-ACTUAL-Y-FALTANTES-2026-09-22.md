# Estado actual y faltantes de PlanCope

**Corte:** 22 de septiembre de 2026. **Alcance:** revisión del repositorio en la rama `GeronimoSerial/cierre-keys` (`639d775`), su documentación, rutas de API, interfaces y pruebas existentes. No equivale a una certificación de producción: en este entorno no están instalados `dotnet` ni `node`, ni hay una base PlanCope conectada.

## Decisiones de alcance

- **Firma Authenticode:** fuera de alcance por decisión del propietario. El pipeline ya admite `allow_unsigned` y rotula las entregas como `UNSIGNED`. No se propone adquirir certificado ni bloquear releases por ausencia de firma. La instalación en Windows debe verificarse como entrega sin firma.
- **Toma de examen:** dentro de alcance, de extremo a extremo, con especial atención al alumno, operador, persistencia sin conexión y recuperación de datos.
- **Población de base:** debe distinguir entre PostgreSQL Central y SQLite Local. No cargar datos nominales en una base sin identificar el entorno y el origen autorizado.

## Qué significa «la app siempre tiene los datos»

El operador no debería elegir una base ni repetir una importación cada vez que abre la app. El contrato operativo propuesto es:

1. **Central conserva** escuelas, usuarios, padrón, exámenes publicados, sesiones recibidas y resultados en PostgreSQL persistente. Migraciones y respaldos se ejecutan como parte del despliegue.
2. **Local conserva** el padrón de su CUE, exámenes descargados, sesiones, respuestas y outbox en la SQLite de `%LocalAppData%\PlanCope\`. Abrir, cerrar o actualizar la aplicación no debe borrar esos datos.
3. **Al activar una escuela**, el paquete cifrado incorpora el padrón de su CUE a SQLite. Después del enrolamiento, el pull de exámenes llena el catálogo local. Mientras no hay red, se usa la última copia válida; al volver la red, se reintentan el pull de exámenes y el push de respuestas.
4. **Datos reales no se inventan:** un examen debe ser creado y publicado en Central, y el padrón de un año debe proceder del paquete autorizado o de GE. Los dos exámenes demo locales permiten probar la pantalla, pero no representan una toma real y deben estar claramente separados del catálogo operativo.

**Brecha para cumplirlo sin intervención:** `SyncBackgroundService` automatiza exámenes y outbox, pero el padrón Central→Local requiere hoy `POST /api/sync/pull-roster` con CUE y año. El paquete cifrado cubre la primera activación sólo si está presente y desbloqueado. Para mantener el padrón actualizado sin una acción manual hace falta derivar CUE y ciclo lectivo desde la identidad/instantánea local, refrescar desde la fuente autorizada con una frecuencia controlada y mostrar la fecha del último padrón válido. Si no existe esa fuente o un examen publicado, la app debe mostrar «faltan datos» y el motivo; nunca afirmar que está lista por tener un demo.

## Mapa del sistema y estado

| Tramo | Implementado en el repositorio | Evidencia | Pendiente para operación real |
|---|---|---|---|
| Autoría | Next.js: alta, versiones, editor de bloques, assets, publicación | `ExamsController`, `exam-builder.tsx`, `publish-panel.tsx` | Crear y publicar un examen real de punta a punta sobre la instancia destino; revisar contenido pedagógico y claves de respuesta. |
| Datos maestros | Entidades de escuelas, usuarios, roles y padrón GE; migraciones EF Core | `PlanCopeDbContext`, `RostersController` | Cargar/verificar escuelas, usuarios y roles en la base destino; ejecutar refresco del padrón con credenciales GE válidas. |
| Activación | Desbloqueo local, claves centrales, enrolamiento y revocación | `ActivationEndpoints`, `EnrolmentEndpoints`, `ActivationAdminController` | Probar en Windows con el paquete de padrón real; definir entrega de claves a cada escuela. |
| Distribución | Pull de exámenes y padrón; sincronización automática | `SyncController`, `LocalExamPullService`, `LocalRosterPullService`, `SyncBackgroundService` | Probar una versión publicada y su destino en un nodo físico con interrupción y recuperación de red. |
| Sesión | Creación, código de acceso, vínculo a sección nominal, pausa/cierre y progreso | `SessionEndpoints`, `SessionsWorkspace.tsx` | Ensayo con docente y sección real; confirmar reglas de cierre y recuperación de sesiones. |
| Alumno | Ingreso por código, resolución por DNI si nominal, confirmación, respuestas, envío | `AttemptEndpoints`, `StudentApp.tsx`, `ExamTakingPanel.tsx` | Prueba de usabilidad y carga simultánea en hardware y red escolar. |
| Corrección y estadísticas | Corrección local, rollups nominales y recálculo central | `AttemptEndpoints`, `StatsRollupRepository`, E2E de B9 | Validar resultados y reportes contra un conjunto real de respuestas; explicar al operador que una sesión no nominal no produce estadísticas. |
| Entrega | Outbox local, push idempotente y recepción central | `LocalOutboxPushService`, `SyncController` | Conciliación operativa: enviados, pendientes, errores y reintentos en despliegue real. |
| Actualizaciones | Feed con gates y rollback en código | `UpdatesController`, Host, E2E de B9 | Prueba de instalación y actualización real en Windows. El feed carece de una superficie HTTP para administrar `release_rings`. |

## Flujo de toma de examen que debe aceptarse

1. Central: crear examen y versión; cargar bloques, imágenes y claves; publicar. Verificar que la versión aparece en el pull del nodo autorizado.
2. Nodo escolar: activar fase A con el padrón cifrado, enrolar fase B con clave, sincronizar examen y padrón. Confirmar CUE, año, sección y cantidad esperada antes de iniciar.
3. Operador: crear sesión nominal, recibir código, observar progreso; probar pausa y reanudación. Mantener Central inaccesible durante la toma.
4. Alumno: ingresar código, escribir DNI, confirmar identidad sin exponer nombres ante una búsqueda fallida, revisar examen, responder, guardar, enviar y recibir comprobante. Repetir con corte de conexión, recarga de pantalla y rechazo de segundo intento nominal.
5. Nodo: comprobar respuesta e intento en SQLite, resultado de corrección, estadística nominal y evento pendiente en outbox. Cerrar la sesión según la política operativa.
6. Reconectar: empujar outbox; comprobar recepción, ausencia de duplicados y coincidencia del resultado recalculado en Central. Conciliar cantidades entre sección, intentos, envíos y resultados.

El E2E documentado en `_briefs/B9-PROGRESS.md` ejercita el recorrido técnico con una red Central que falla durante la ventana offline y compara resultados Local/Central. Eso prueba integración de servidores y persistencia de la prueba; falta repetir el recorrido con UI, instalador, red y máquinas de escuela reales.

## Faltantes priorizados

### P0 — antes de una toma real

0. **Crear el primer administrador en producción.** `DevelopmentSeeder` se ejecuta sólo bajo `ASPNETCORE_ENVIRONMENT=Development`; no hay alta pública de usuarios. Sin una cuenta previamente cargada, el login y las pantallas de claves quedan en un círculo cerrado. Preparar un bootstrap de una sola ejecución con credenciales entregadas fuera del repositorio, que no cree duplicados ni reemplace contraseñas existentes; luego emitir claves desde la UI. Nunca habilitar el seeder de desarrollo en producción.
   **Hallazgo adicional:** el administrador de desarrollo original tampoco podía emitir claves: `AuthController` asigna alcance escolar al rol `Admin`, pero `DevelopmentSeeder` no le asignaba ningún CUE; `ActivationAdminController` rechaza un emisor de alcance escolar sin CUE. El seeder de desarrollo de este checkout incorpora una escuela y una asignación ficticias para cerrar ese recorrido local.
1. **Poblar y auditar la base destino.** Identificar instancia y respaldo; aplicar migraciones; cargar escuelas/CUE, usuarios y asignaciones, padrón del año, examen publicado y destinos. Comprobar conteos, unicidad de CUE y acceso por rol. El `DevelopmentSeeder` sólo crea un administrador de prueba en modo Development; no es un cargador de producción.
2. **Ensayo operativo completo de examen nominal.** Usar un nodo Windows, padrón real autorizado y un examen representativo. Registrar tiempos, caídas de red, reingreso, cierre y conciliación posterior. El test E2E no sustituye esta aceptación.
3. **Distribución de claves de activación.** Definir emisión, entrega individual, custodia, revocación y reposición por escuela. El límite `max_activations` reduce impacto pero no resuelve la entrega.
4. **Verificar respaldo y recuperación.** Restaurar una copia Central y una SQLite local en un entorno de prueba; comprobar que los intentos pendientes sobreviven y llegan una sola vez.
5. **Corregir readiness.** `/health/ready` comprueba sólo `CanConnectAsync`. En un PostgreSQL vacío respondió `200` mientras `POST /api/auth/login` devolvió `500` por ausencia de `core.users`. Debe comprobar que la migración esperada está aplicada antes de anunciar la API lista.

### P1 — robustez y observabilidad

0. **Administración de usuarios y escuelas en Central.** El menú Central y los controladores inspeccionados no ofrecen alta de usuarios, roles, asignaciones CUE ni escuelas. Para operar después del primer bootstrap hace falta una superficie administrativa con autorización y auditoría, o un importador operativo documentado. Esto impide delegar acceso sin intervención directa en la base.
5. **Cerrar el test omitido de enrolamiento.** `EnrolmentEndpointsTests.Redeem_endpoint_placeholder` está saltado; la razón indicada quedó obsoleta al existir fixtures con HTTP saliente simulado.
6. **Cubrir ramas de la UI de alumno y cliente HTTP.** `QuestionNav`, `QuestionTitle`, `SubmitConfirmDialog`, `ExamBlock` y `studentApi` figuran como no probados en el cierre B9. Priorizar guardado, reintento y errores de envío.
7. **Validar reglas del servidor al guardar y enviar.** `PUT /api/attempts/{id}/answers` comprueba el estado del intento, pero no consulta el estado actual de la sesión; `POST .../submit` tampoco lo rechaza explícitamente. Decidir si pausa/cierre deben impedir estas operaciones y fijarlo con pruebas.
8. **Administrar anillos de actualización.** El feed consume `release_rings`, pero la configuración se hizo directamente en DbContext en el E2E; falta flujo administrativo verificable.

### P2 — validación de campo y limpieza documental

9. **Medir hardware de referencia.** Arranque frío, render de 100 preguntas, CPU en reposo y Argon2id en Windows de 2 núcleos, 4 GB y HDD (`docs/reference-profile.md`).
10. **Probar actualización instalada.** Descargar, verificar, aplicar, reiniciar, rollback y preservar `%LocalAppData%\PlanCope\` con un paquete Velopack real y sin firma.
11. **Actualizar documentación histórica.** `docs/REMAINING-WORK.md` mezcla el estado del 16/9 con afirmaciones posteriores; por ejemplo, dice que Central no está desplegado mientras `README.md` declara release `0.1.0` en producción. Usar este documento como corte actual y comprobar el estado del servicio mediante acceso operativo.

## Población de datos: criterio de ejecución

| Base | Estado observado aquí | Carga apropiada |
|---|---|---|
| PostgreSQL Central | No hay conexión PlanCope configurada ni contenedor PlanCope activo. `nal-db` es MySQL de otro proyecto. | Migraciones EF Core primero; luego datos maestros autorizados mediante servicios/importadores y publicación por API. Evitar insertar manualmente exámenes publicados y padrones, porque requieren relaciones, checksums y paquetes de sincronización. |
| SQLite Local | No hay archivo SQLite PlanCope en este workspace. La API crea esquema DbUp y dos exámenes demo al iniciar (`LocalDemoExamSeeder`, predeterminado `Local:SeedDemoExam=true`). | Para prueba local, iniciar API/Host, activar, sincronizar examen y padrón; desactivar demos para ensayo real. |
| Archivo de padrón | Existe un ZIP de 12 MB fuera del worktree, citado por el proyecto; su contenido no se importó ni se copió. | Verificar procedencia y alcance; usar el flujo cifrado/por CUE previsto. No volcar datos de menores en scripts, logs ni documentación. |

**Criterio de finalización de la carga:** conteos esperados por entidad y CUE, integridad referencial, examen visible en Central, paquete descargado en Local, sección nominal seleccionable, un intento de prueba enviado y conciliado. Registrar fecha, entorno, origen, operador y resultado de cada paso.

## Evidencia y límites de esta revisión

- Estado del árbol: sin cambios previos observados. Último commit inspeccionado: `639d775`.
- No se ejecutaron compilación ni tests nuevos porque `dotnet` y `node` no están en el PATH de este entorno. El cierre previo registró 497 pruebas aprobadas y una omitida en `docs/REMAINING-WORK.md`; esos resultados son históricos, no una ejecución de hoy.
- No se consultó ni se escribió la base de producción: no hay conexión PlanCope de producción identificada en este workspace. La base Docker de la comprobación local está aislada y aún no contiene datos de la aplicación.

### Comprobación local posterior

Se inició PostgreSQL 17 en un volumen Docker aislado (`plancope-local-data`), la web compilada de este checkout en `localhost:3000` y la API publicada `ghcr.io/geronimoserial/plan-cope-central-api:latest` en `localhost:8081` (su imagen declara revisión `unknown`, por lo que no acredita identidad con este commit). `GET /login` devolvió `200`; `GET /health/live` y `/health/ready` devolvieron `200`. Con el esquema aún sin migrar, `POST /api/auth/login` devolvió `500` con PostgreSQL `42P01: relation "core.users" does not exist`. La imagen de migraciones no terminó de descargarse del registro después de varios intentos, uno de ellos con timeout TLS; se interrumpió la descarga. Este resultado prueba el defecto de readiness, no el login después de migrar. Se corrigió el endpoint en el checkout para verificar migraciones pendientes, pero esa corrección no pudo compilarse ni ejecutarse aquí porque no hay SDK .NET local y la imagen SDK tampoco terminó de descargarse.

### Datos de revisión de Central

`scripts/seed-central-dev.py` prepara por API un examen ficticio con versión publicada y una clave de activación de prueba. Exige una URL HTTP de `localhost`, reutiliza el administrador que crea `DevelopmentSeeder`, evita duplicar el examen o la clave, y guarda el texto de la clave en un archivo de permisos `0600`. `DevelopmentSeeder` agrega el CUE `180000100` y una escuela ficticia sólo en Development, para que ese administrador tenga alcance sobre claves. No crea nodos ni resultados falsos: esas pantallas deben revisarse también en su estado vacío y luego con datos generados por el flujo real de activación y toma. La ejecución efectiva del script requiere primero migrar la base y correr una API compilada con el seeder actualizado.

Una vez aplicada la migración y levantada esa API:

```bash
PLANCOPE_DEV_ADMIN_PASSWORD='Admin123!' python3 scripts/seed-central-dev.py --api-url http://127.0.0.1:8081
```

La contraseña mostrada es exclusivamente la predeterminada de `appsettings.Development.json`. El script falla si no hay login; no crea una cuenta por fuera de la API. La clave emitida se guarda en `/tmp/plancope-central-demo-key` y no debe reutilizarse fuera del entorno local.
