# Cierre de claves y administración Central — estado y continuidad

**Corte:** 22 de septiembre de 2026. **Rama:** `GeronimoSerial/cierre-keys`.
**Documento de referencia previo:** `docs/ESTADO-ACTUAL-Y-FALTANTES-2026-09-22.md`.

Este documento describe qué se cerró en esta tanda, qué quedó abierto y qué necesita saber
quien continúe. No es una certificación de producción: la verificación de extremo a extremo
con instalador, red escolar y hardware real sigue pendiente.

## Problema de partida

El sistema Central no permitía gestionar usuarios ni crear claves de activación. La causa no
era una sola: eran cuatro problemas encadenados, tres de código y uno de datos.

## Qué se cerró

### B0 — Carga de datos maestros en Central

`scripts/seed-central-master-data.sh` y `.sql` cargan `core.provinces`, `core.departments`,
`core.localities` y `core.schools` desde la base GE `asistencias`, tabla `public.secciones`.

La carga es idempotente (`WHERE NOT EXISTS` sobre las claves naturales), toma credenciales
únicamente de variables de entorno `PG*`, y corre dentro de una sola transacción que termina
en `ROLLBACK`. **El script no expone bandera de commit**: no puede aplicar cambios por
accidente.

**Ejecutado contra producción el 22/9/2026 con autorización del propietario.** Conteos
verificados releyendo la base después del commit:

| Tabla | Filas |
|---|---|
| `core.provinces` | 5 |
| `core.departments` | 117 |
| `core.localities` | 848 |
| `core.schools` | 2058 |
| `roster.*` | 0 (fuera de alcance por decisión) |
| `core.user_schools` | 0 (sin tocar) |

Respaldo previo: `/home/gero/orca/backups/plan_cope-pre-seed-20260922-113341.sql`.

El commit se aplicó tomando el artefacto exacto que produjo el dry-run verificado y
cambiando una sola línea (`ROLLBACK;` → `COMMIT;`) en una copia fuera del repositorio. El
script versionado conserva su garantía de sólo-dry-run.

### B1 — Primer administrador de producción

`AdminBootstrapper` crea la primera cuenta administradora desde configuración
(`PLANCOPE_BOOTSTRAP_ADMIN_EMAIL`, `_PASSWORD`, `_FULL_NAME`).

Es **opt-in**: sin ninguna variable presente registra un mensaje informativo y el arranque
continúa normalmente — ese es el camino de todo reinicio ordinario. Con alguna presente, el
despliegue está pidiendo un bootstrap, así que valida estricto y falla si algo falta, está en
blanco, tiene menos de 12 caracteres o es una contraseña por defecto conocida.

Es idempotente: un usuario existente nunca se modifica, su hash de contraseña queda intacto,
y sólo se asegura la asignación del rol `Admin`, lo que además repara una corrida previa a
medio aplicar. La contraseña no se registra en ningún camino. `DevelopmentSeeder` sigue
siendo exclusivo de Development.

Operación documentada en `docs/central-admin-bootstrap.md`.

### B2 — Emisión de claves de activación

`AuthController` otorga alcance provincial únicamente al rol `RosterProvince`, de modo que un
`Admin` caía en alcance escolar y `ResolveIssuedForCueAsync` lo rechazaba por no tener
`roster_cue`. Un administrador Central no podía emitir, listar ni revocar claves.

La administración de claves ahora se decide por `HasUnboundedKeyScope()` — rol `Admin`, o
alcance provincial de padrón — en lugar de leer el claim crudo. El helper es privado del
controller para que no pueda ampliar el acceso a datos de padrón: **emitir una clave de
activación universal y leer padrón de menores son autoridades deliberadamente separadas**.
`AuthController`, `TokenService`, `RosterScopeAuthorizationHandler` y los controllers de
padrón quedaron sin cambios.

### B3 — Superficie administrativa Central

`api/admin/schools`, `api/admin/users` y `api/admin/roles`, con contratos DTO compartidos en
`PlanCope.Shared.Contracts/Admin`, auditoría en toda mutación y reuso de `CueCode`.

La autorización mantiene separadas las mismas dos autoridades que B2: administrar identidades
pasa por `HasUnboundedAdminScope()`, mientras que leer padrón sigue pasando por
`RosterScopeRequirement` sin cambios.

La asignación de roles no puede usarse para escalar privilegios: `UnboundedScopeRoles` lista
los roles que confieren autoridad irrestricta (`Admin`, `RosterProvince`) y un llamador sin
esa autoridad no puede otorgarlos — incluido a su propio usuario.

## Verificación

139 pruebas en verde sobre la rama consolidada (`PlanCope.Central.Api.Tests`), frente a 103
en la base. Cada batch se compiló y probó por separado y luego en conjunto tras los merges.

**Las pruebas en verde no fueron suficientes.** Tres defectos reales pasaron los tests de su
propio batch y se detectaron leyendo el diff:

1. **B1 — caída de producción al arrancar.** El bootstrap se invocaba incondicionalmente y
   lanzaba excepción sin configuración. La API desplegada no tiene esas variables: el
   despliegue habría impedido el arranque. 110 pruebas en verde sobre ese cambio.
2. **B3 — escalación de privilegios.** La asignación de roles aceptaba cualquier rol con la
   única guarda de compartir CUE con el usuario destino. Como el propio usuario comparte su
   CUE, un usuario de alcance escolar podía asignarse `RosterProvince` y pasar a leer el
   padrón provincial completo. 118 pruebas en verde sobre ese código.
3. **B0 — identificadores sin comillas.** EF Core crea las columnas como PascalCase entre
   comillas; PostgreSQL pliega a minúsculas un identificador sin comillas. El script fallaba
   con `column "id" does not exist`.

Criterio que conviene conservar: **para autorización, sólo los casos negativos afirmados son
evidencia**. Las pruebas de B3 afirman `ReturnsForbiddenAndWritesNoRow` — comprueban que la
fila no se escribió, no sólo el código de respuesta — e incluyen un caso positivo que prueba
que la restricción no se pasó de larga.

## Qué queda abierto

### Pendiente inmediato

- **B4 — UI Central de administración.** No iniciado. Debe construirse sobre los contratos
  DTO de `PlanCope.Shared.Contracts/Admin` y sobre el modelo de alcance de B2. Cubre alta de
  escuelas, usuarios, roles y asignaciones CUE, más emisión, revocación y reposición de
  claves.
- **B5 — Verificación de extremo a extremo.** No iniciado. Recorrido: migrar → bootstrap →
  alta de escuela y usuario por la API → emitir clave → login por cada rol → conciliación.

### Datos

- **`core.user_schools` está en 0.** Ningún usuario tiene asignación de escuela. Con B2 el
  administrador ya no la necesita para emitir claves, pero los usuarios de alcance escolar sí
  la necesitan para operar. La API de B3 permite asignarlas.
- **Una escuela quedó sin cargar:** CUE `1801096-00`, ESCUELA Nº 225 "CESAREO NAVAJAS
  CENTENO". Sus 6 secciones en GE tienen `departamento_id`, `departamento` y `localidad_id` en
  `NULL`, y no tiene ninguna otra sección con geografía válida. Como `core.schools.LocalityId`
  es `NOT NULL`, no puede insertarse. Corregir la geografía en GE y volver a correr el script;
  al ser idempotente sólo agregará la faltante.
- **Padrón de alumnos no cargado.** Fase 1 excluyó deliberadamente `roster.*`. Cargarlo
  implica datos de menores y requiere decisión explícita sobre ciclo lectivo (2025, 2026 o
  ambos) y sobre la ruta autorizada de ingreso.

### Deuda declarada por los batches

- **B2:** `ListNodes` y `RevokeNode` tienen la misma forma de bug de alcance un nivel más
  abajo. El seed de CUE del `DevelopmentSeeder` quedó redundante.
- **P1 del documento previo:** sigue abierto el test omitido de enrolamiento, la cobertura de
  UI de alumno, la validación del estado de sesión al guardar y enviar, y la administración de
  `release_rings`.

## Notas operativas para quien continúe

- **`global.json` fija SDK `10.0.301`** con `rollForward: latestFeature`, aunque todos los
  proyectos apuntan a `net8.0`. Instalar sólo el SDK 8 no alcanza.
- **No compilar la solución entera en Linux:** `PlanCope.Local.Host` apunta a `net8.0-windows`.
  Compilar y probar proyectos puntuales.
- **Trampa de identificadores:** cualquier SQL escrito a mano contra este esquema debe usar
  comillas dobles en los nombres de columna creados por EF Core.
- **Geografía por id, no por nombre:** contar `departamento` y `localidad` distintos por nombre
  subcuenta gravemente (25 y 533) frente a contar por id (118 y 849); los nombres se repiten
  entre jurisdicciones.
</content>
