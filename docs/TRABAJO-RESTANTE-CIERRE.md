# Trabajo restante para el cierre operativo

**Corte:** 22 de septiembre de 2026. Continúa `docs/CIERRE-KEYS-ESTADO-Y-CONTINUIDAD.md`,
mergeado en `main` por el PR #51.

Este documento existe para que el siguiente tramo arranque sin reconstruir contexto. Lo que
ya está cerrado no se repite acá.

## Estado de partida

Ya está en `main` y verificado: la carga de datos maestros, el bootstrap del primer
administrador, la emisión de claves de activación sin asignación de CUE, y la API de
administración de escuelas, usuarios, roles y asignaciones. 139 pruebas en verde.

La base Central de producción ya tiene 5 provincias, 117 departamentos, 848 localidades y
2058 escuelas. `roster.*` y `core.user_schools` siguen en cero.

## B4 — Interfaz de administración en Central

**No iniciado.** Depende de la API ya mergeada.

- Construir contra los contratos de `src/Shared/PlanCope.Shared.Contracts/Admin/`. No
  redefinir DTOs en el cliente.
- Cubrir: alta y edición de escuelas; alta, baja y reseteo de contraseña de usuarios;
  asignación y revocación de roles y de CUE; emisión, listado, revocación y reposición de
  claves de activación.
- Respetar el modelo de alcance del backend en la propia interfaz: un usuario sin autoridad
  irrestricta no debe ver acciones que el servidor le va a rechazar. La autorización real es
  la del servidor; la interfaz sólo evita el callejón sin salida.
- Pantallas vacías: la interfaz debe ser legible con cero nodos registrados y cero claves
  emitidas, que es el estado actual de producción.

## B5 — Verificación de extremo a extremo

**No iniciado.** Depende de B4.

Recorrido mínimo a acreditar, sobre un entorno identificado y registrando fecha, entorno,
operador y resultado de cada paso:

1. Aplicar migraciones sobre la base destino y comprobar que `/health/ready` pasa de 503 a 200.
2. Ejecutar el bootstrap con `PLANCOPE_BOOTSTRAP_ADMIN_*` y verificar que una segunda
   ejecución no duplica ni modifica nada.
3. Crear una escuela y un usuario desde la interfaz, asignarle rol y CUE.
4. Emitir una clave de activación con el administrador y verificar que queda auditada.
5. Iniciar sesión con cada rol y comprobar que el alcance de cada uno es el esperado,
   incluidos los casos que deben ser rechazados.
6. Activar un nodo con la clave emitida y conciliar.

El E2E existente en `_briefs/B9-PROGRESS.md` ejercita el recorrido técnico entre servidores.
No sustituye esta aceptación con interfaz, instalador, red y máquinas reales.

## Pendientes puntuales

- **Escuela sin geografía.** CUE `1801096-00`, ESCUELA Nº 225 "CESAREO NAVAJAS CENTENO". Sus
  seis secciones en GE tienen `departamento_id`, `departamento` y `localidad_id` en `NULL` y
  no hay ninguna otra sección válida para ese CUE. Corregir en GE y volver a correr
  `scripts/seed-central-master-data.sh`; el script es idempotente y sólo agregará la faltante.
- **Camino de commit revisado para la carga.** El script versionado es deliberadamente
  sólo-dry-run y no expone bandera de commit. La carga a producción del 22/9 se aplicó desde
  una copia fuera del repositorio con una única línea cambiada (`ROLLBACK;` → `COMMIT;`). Para
  la próxima carga conviene agregar un `--commit` explícito y revisado, de modo que lo que se
  ejecute sea exactamente lo que se revisó.
- **`core.user_schools` está en cero.** Ningún usuario tiene asignación de escuela. El
  administrador ya no la necesita para emitir claves, pero los usuarios de alcance escolar sí
  la necesitan para operar.
- **Padrón de alumnos.** Fase 1 lo excluyó deliberadamente. Cargarlo implica datos de menores
  y requiere decidir ciclo lectivo (2025, 2026 o ambos) y la vía autorizada de ingreso.
- **Deuda declarada por B2.** `ListNodes` y `RevokeNode` tienen la misma forma de bug de
  alcance un nivel más abajo. El seed de CUE del `DevelopmentSeeder` quedó redundante.
- **Contraseña de desarrollo en el repositorio.** `Admin123!` está en
  `appsettings.Development.json` y en `DevelopmentSeeder.cs` desde antes de este tramo, y el
  escaneo de secretos la marca. Las apariciones en `AdminBootstrapper.cs` y sus pruebas son
  deliberadas: son la guarda que rechaza esa contraseña. Conviene decidir si el valor de
  desarrollo se mueve a configuración local no versionada.

## Pendientes heredados del documento previo

Siguen abiertos, de `docs/ESTADO-ACTUAL-Y-FALTANTES-2026-09-22.md`: el test omitido de
enrolamiento, la cobertura de la interfaz de alumno y el cliente HTTP, la validación del
estado de sesión al guardar y enviar respuestas, y la administración de `release_rings`.
</content>
