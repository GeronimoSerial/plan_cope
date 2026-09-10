# Plan de ejecución por batches: CI/CD, empaquetado y desacople — Plan Cope

Este documento opera el plan descripto en `ci-cd-velopack-coolify-plan.md` y lo convierte en una
secuencia de batches ejecutables por un equipo de agentes. Reemplaza a ese documento como fuente
de verdad operativa; el plan original queda como referencia de alto nivel.

Modelo de ejecución: cada batch se ejecuta dentro de un worktree nuevo creado con `orca`. Claude
Sonnet actúa como líder de equipo: lee el batch, lo descompone en tareas atómicas, despacha cada
tarea a `opencode` corriendo `opencode-go/deepseek-v4-flash`, revisa el resultado contra un
criterio de aceptación verificable y corrige antes de cerrar el batch.

---

## 1. Objetivo y criterio de finalización

**Objetivo**: desde GitHub, ejecutar un workflow indicando una versión y obtener, sin intervención
manual adicional:

- dos imágenes verificadas en GHCR (`plan-cope-central-api`, `plan-cope-central-web`);
- Central API + Central Web desplegados y saludables en Coolify (`coolify.sistemas.mec.gob.ar`);
- un instalador Velopack de `PlanCope.Local.Host` firmado, publicado en un canal **privado** (no
  en un asset público de GitHub Releases) y descargable desde `/descargas` en Central Web detrás
  de sesión autenticada;
- una instalación existente de la versión anterior detecta y aplica la actualización sin perder
  SQLite, assets, configuración ni claves locales;
- el padrón nominal (1440 CUEs, 13429 secciones, 227598 estudiantes) viaja embebido y cifrado en
  el instalador, y sólo el CUE que el operador tipea en el momento se descifra e importa;
- las tres brechas de autorización verificadas (school_id nulo, rosters sin control por CUE, roles
  sin aplicar) están cerradas con pruebas automatizadas que las cubren;
- la autoría de exámenes ya no existe en el Host de escritorio; sólo administra sesiones.

**Criterio de finalización verificable**: `gh workflow run release.yml -f version=X.Y.Z -f
channel=stable -f target=production` termina en verde y dentro de los 20 minutos posteriores
`GET https://api.plancope.sistemas.mec.gob.ar/health` responde `200` con el commit esperado en el
cuerpo, y `GET https://plancope.sistemas.mec.gob.ar/descargas` (Central Web) exige sesión y sirve
el instalador de esa versión.

---

## 2. Decisiones tomadas

Estas decisiones están cerradas. Ningún batch debe reabrirlas ni ofrecer alternativas; toda tarea
delegada debe citar el número de decisión que la justifica cuando corresponda.

| # | Decisión | Razón |
|---|----------|-------|
| 1 | Un único `.exe` universal; el operador tipea el CUE en el momento | Evitar 1440 builds; el binario es idéntico en todas las escuelas |
| 2 | El padrón viaja con el instalador, cifrado, fuera de `PlanCope.Local.Api.dll` | Las escuelas tienen conectividad no confiable; permite refrescar el padrón sin recompilar |
| 3 | Envelope encryption por CUE: AES-256-GCM por escuela, DEKs envueltas con clave maestra derivada por Argon2id | Sólo se descifra el CUE en uso; los otros 1439 quedan protegidos en reposo |
| 4 | Activación única por máquina vía passphrase + Windows DPAPI; después sólo se tipea el CUE | Balance de fricción/seguridad para un equipo que viaja a escuelas |
| 5 | El CUE nunca es la clave de descifrado | Los CUEs son secuenciales y públicos; usarlos como clave expondría los 227598 registros |
| 6 | El instalador con el padrón no se publica como asset público de GitHub Releases | El repositorio es público; un asset de Release es descargable sin autenticación |
| 7 | Firma de código: PFX autofirmado en GitHub Secrets | Decisión de partida; deja advertencia de SmartScreen documentada, con upgrade futuro a Azure Trusted Signing |
| 8 | Autenticación: se extiende la autenticación .NET existente | Ya hay JWT propio, BCrypt y roles; no se introduce Better Auth ni una segunda identidad |
| 9 | Central API y Central Web se despliegan al mismo servidor Coolify; GitHub Actions construye y publica en GHCR, Coolify sólo consume | Separación build/deploy; el token de Coolify nunca ve una escuela |
| 10 | La autoría de exámenes se elimina del Host de escritorio; sólo vive en Central Web | El acoplamiento es superficial (2 archivos, 1 endpoint) y no hay canal de sincronización de vuelta a Central |
| 11 | Datos persistentes se mueven a `%LocalAppData%\PlanCope\` con `PLANCOPE_DATA_DIR` y migración única de una base legada | Las actualizaciones de Velopack reemplazan binarios, no deben tocar datos |
| 12 | Velopack para actualizaciones de escritorio, `win-x64` self-contained, canales `stable`/`beta` | Estándar de actualización sin fricción para instalaciones offline-first |
| 13 | El alcance de acceso al padrón se modela con un claim `roster_scope` (`province` \| `school`): Planeamiento accede a los 1.440 CUEs por rol, las escuelas sólo a los suyos vía `user_schools` | Confirmado por el usuario. El alcance provincial es un rol, no una fila comodín en `user_schools`: así se audita quién lo tiene y se revoca sin tocar datos. Denegar por defecto si falta el claim |
| 14 | La base de datos de producción es un Huawei Cloud RDS externo para PostgreSQL 17.9 (endpoint y credenciales fuera de este repositorio: es publico), no un Postgres administrado por Coolify | Coolify eliminó el Postgres administrado que tenía; la base ya fue migrada y verificada (30 tablas en los schemas `core`/`exam`/`roster`/`sync`/`audit`/`publication`/`settings`, `__EFMigrationsHistory` en `public`, cero datos nominales) contra el RDS real |
| 15 | Los dominios de producción son fijos: Central API en `https://api.plancope.sistemas.mec.gob.ar`, Central Web en `https://plancope.sistemas.mec.gob.ar`, ambos bajo el certificado wildcard `*.sistemas.mec.gob.ar` | Ya provisionados y verificados en Coolify (proyecto `bpucczfs7kutjdavy3qoieha`, ambiente `mapqyaant3x520pw4kp57lhh`, servidor `nm2rc1csc3gzm0n3tkt95g5q`) |
| 16 | Coolify sólo consume imágenes (`docker-image` apps) publicadas en GHCR por `release.yml`; nunca construye nada. La conexión al RDS usa `SSL Mode=VerifyCA;Root Certificate=/etc/ssl/certs/huawei-rds-ca.pem` (CA público de Huawei, committeado en `deploy/certs/huawei-rds-ca.pem`, copiado a esa ruta exacta en la imagen); `sslmode=verify-full` no es alcanzable porque el certificado del servidor RDS está emitido para la dirección interna de la instancia y no para el endpoint que se marca desde afuera | El certificado del servidor RDS está emitido para la dirección interna de la instancia, no para el endpoint público; por eso se usa la CA pública de Huawei (`VerifyCA`) y no `verify-full` |

---

## 3. Modelo de amenaza y datos sensibles

**Activo protegido**: `global-2026-padrones-completo.zip` (12 MB comprimido / 64.2 MB
descomprimido), 1442 entradas → 1440 archivos `<CUE>-2026.roster.json` + `sync-summary-2026.json`
+ `escuelas-sin-padron.txt`. Cada registro de estudiante contiene `document` (DNI), `firstName`,
`lastName`: son datos personales de menores.

**Por qué el CUE no puede ser la clave (decisión 5)**: los CUEs son secuenciales
(`180000100`, `180000200`, `180000300`, …) y son información pública (identifican
establecimientos educativos, no personas). Si el CUE derivara o destrabara la clave de
descifrado, un actor con el binario público podría iterar 1440 valores conocidos y volcar los
227598 registros sin ninguna otra credencial. La clave debe depender de un secreto que **no**
esté en el binario ni sea derivable de datos públicos: la passphrase de activación.

**Reglas de manejo del zip fuente**:

- El zip **nunca** se coloca dentro del worktree de trabajo (ver Batch 0, precondición de
  ubicación). Vive en `/home/gero/orca/plan_cope/global-2026-padrones-completo.zip`, fuera del
  árbol versionado.
- Ningún comando de un agente debe copiar, mover ni referenciar ese zip desde dentro de un
  worktree. Las tareas de Batch 2 que necesiten datos de prueba usan una muestra sintética de 2-3
  CUEs generada localmente, nunca el zip completo.
- `.gitignore` ya protege `*.roster.json`, `*.roster.enc`, `PlanCope-padron-*.zip`, `secrets/`
  (verificado en este repo). Ningún batch debe remover estas líneas.
- Nada derivado del padrón (JSON crudo, bundle cifrado, claves, manifiestos con nombres) se sube
  a un artifact de CI, a un log de GitHub Actions ni a un comentario de PR. Los tests de Batch 2
  usan fixtures sintéticas, no el dataset real.
- El dataset real sólo se manipula en la máquina de quien prepara el release (fuera del alcance de
  los agentes automatizados), usando la herramienta de empaquetado del Batch 2 en modo local.

**Qué nunca debe llegar a un artifact público**: DNIs en claro, el zip fuente, bases SQLite con
datos importados, la `Nominalization__DocumentHmacKey`, la passphrase de activación o las claves
maestras derivadas, el PFX de firma y su contraseña.

---

## 4. Arquitectura de entrega

```text
                                   PR
                                   |
                                   v
                    .github/workflows/ci.yml (obligatorio)
                    - .NET (Windows): build, tests, Host build
                    - JS (Linux): npm ci, vitest, builds
                    - Contenedores: build sin push, compose config, smoke
                    - Seguridad: SBOM, scans, attestations
                                   |
                                   v  (merge a main)
                    workflow_dispatch: release.yml (version, channel, target)
                                   |
                    +--------------+--------------------------+
                    |                                          |
                    v                                          v
        Central API/Web -> build -> GHCR                Host Windows -> build ->
        (ghcr.io/geronimoserial/                          Velopack pack + sign
         plan-cope-central-api,web)                              |
                    |                                            v
                    v                                  Instalador privado (NO
        Coolify deploy trigger                          asset público de Release)
        (coolify.sistemas.mec.gob.ar)                            |
                    |                                            v
                    v                                  Storage privado (ver
        Health check + smoke test                       Preguntas abiertas #2)
                    |                                            |
                    v                                            v
        GitHub Release publicado           Central Web /descargas (autenticado)
        (sólo metadata, SBOM, checksums)          |
                                                   v
                                       Operador de escuela descarga
                                       instalador con padrón cifrado embebido
```

---

## 5. Protocolo de delegación

### 5.1 Roles

- **Sonnet (líder de equipo)**: lee el batch de este documento, lo descompone en tareas
  `B<n>.T<m>` si hace falta más granularidad que la aquí listada, despacha cada tarea, ejecuta el
  comando de aceptación, decide aprobar/corregir/escalar, y no cierra el batch hasta que el
  usuario lo revisó.
- **`opencode-go/deepseek-v4-flash`**: ejecuta una tarea atómica por invocación. No tiene memoria
  de tareas anteriores salvo que se use `-c/--continue` explícitamente sobre la misma sesión.
- **`opencode-go/deepseek-v4-pro`**: reservado para tareas de revisión/corrección cuando flash
  falla dos veces seguidas en la misma tarea.

### 5.2 Invocación exacta

Tarea nueva (sesión fresca, siempre):

```bash
opencode run "<prompt de la tarea>" \
  -m opencode-go/deepseek-v4-flash \
  --dir <worktree-path> \
  --format json
```

Corrección sobre la misma tarea (falla de aceptación):

```bash
opencode run "<error exacto del comando de aceptación>" \
  -m opencode-go/deepseek-v4-flash \
  --dir <worktree-path> \
  -c \
  --format json
```

Escalamiento tras 2 fallas consecutivas de la misma tarea:

```bash
opencode run "<prompt original + historial de errores>" \
  -m opencode-go/deepseek-v4-pro \
  --dir <worktree-path> \
  --format json
```

Si `deepseek-v4-pro` también falla, Sonnet ejecuta la tarea directamente (sin opencode) y lo
registra como excepción en el reporte del batch.

**Regla de sesión**: una tarea atómica = una sesión nueva de opencode. Nunca reutilizar `-c` entre
tareas distintas (`B<n>.T<m>` distinto). `-c/--continue` se reserva exclusivamente para iterar
sobre la misma tarea tras una falla de aceptación.

### 5.3 Plantilla de prompt para tareas deepseek

```text
Rol: Ingeniero de software ejecutando una única tarea atómica dentro de un worktree git.

Contexto (leer antes de tocar nada):
- <archivo 1 a leer>
- <archivo 2 a leer>
- ...

Cambio único a realizar:
<descripción de una sola cosa; si hacen falta dos cambios independientes, son dos tareas>

Restricciones:
- No modifiques ningún archivo fuera de: <lista explícita de paths permitidos>
- No agregues dependencias nuevas salvo que se indique explícitamente aquí.
- No cambies el estilo ni el formato de código no relacionado con esta tarea.
- No hagas commit ni push. El líder de equipo revisa y confirma.

Criterio de aceptación (debe salir con código 0):
<comando exacto>

Si el criterio de aceptación falla, no se considera terminada la tarea.
```

### 5.4 Loop de revisión

```text
1. Despachar tarea (sesión nueva) con la plantilla de 5.3.
2. Ejecutar el comando de aceptación.
3. Si pasa -> revisar diff manualmente (Sonnet) -> si es correcto, marcar B<n>.T<m> hecho.
4. Si falla -> `opencode run "<salida exacta del error>" -c ...` (misma sesión, flash).
5. Repetir paso 2-4 hasta 2 intentos fallidos totales en la misma tarea.
6. Al tercer intento -> escalar a deepseek-v4-pro con prompt original + historial de errores.
7. Si deepseek-v4-pro falla -> Sonnet ejecuta la tarea directamente y lo anota como excepción.
8. Un batch no cierra hasta que:
   a. todos sus criterios de salida (sección por batch) pasan, Y
   b. el usuario revisó y aprobó explícitamente el batch.
```

---

## 6. Batches

Convención de numeración: `B<n>.T<m>`. Cada tarea indica archivos objetivo, el cambio único y el
comando de aceptación. Las dependencias entre batches están declaradas en cada uno.

### Batch 0 — Worktree y andamiaje de delegación

**Depende de**: nada (batch inicial).
**Objetivo**: tener un worktree nuevo, reproducible, con `orca.yaml`, script de setup y el shim de
CLI de orca operativos, antes de delegar cualquier tarea de código.

**Precondiciones**:
- Orca runtime corriendo (`orca open` ejecutado manualmente antes de este batch; los comandos
  `orca worktree create` requieren runtime activo).
- El zip `global-2026-padrones-completo.zip` permanece en
  `/home/gero/orca/plan_cope/global-2026-padrones-completo.zip`, **fuera** de cualquier worktree
  (ver sección 3).

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B0.T1 | `~/.local/bin/orca-cli` (nuevo) | Crear shim ejecutable que invoque `ELECTRON_RUN_AS_NODE=1 /home/gero/.local/apps/orca/squashfs-root/orca-ide /home/gero/.local/apps/orca/squashfs-root/resources/app.asar.unpacked/out/cli/index.js "$@"` | `orca-cli --help` sale con código 0 |
| B0.T2 | `/home/gero/orca/plan_cope/orca.yaml` (nuevo) | Crear con `scripts.setup: bash scripts/orca-worktree-setup.sh` y `defaultTabs` mínimo (`Workspace`, sin comando en el primer tab) | `test -f orca.yaml` sale 0 y el YAML parsea (`python3 -c "import yaml,sys; yaml.safe_load(open('orca.yaml'))"` sale 0) |
| B0.T3 | `/home/gero/orca/plan_cope/scripts/orca-worktree-setup.sh` (nuevo) | Script one-shot que restaura tools .NET (`dotnet tool restore` si hay manifest) y ejecuta `npm ci` en la raíz (workspaces `src/Central/PlanCope.Central.Web` y `src/Local/PlanCope.Local.Host/ClientApp`), y termina con `exit 0` explícito | `bash scripts/orca-worktree-setup.sh; echo $?` imprime `0` |
| B0.T4 | — (comando, no archivo) | Ejecutar `orca-cli worktree create --name plan-cope-cicd --repo plan_cope --base-branch main --setup run --json` | La salida JSON contiene un `path` de worktree existente (`test -d <path>` sale 0) |
| B0.T5 | — (verificación) | Dentro del worktree nuevo: `dotnet build PlanCope.sln` (o el `.sln`/`.csproj` raíz que exista) | Sale con código 0 |
| B0.T6 | — (verificación) | Dentro del worktree nuevo: `npm ci && npm run build --workspaces --if-present` | Sale con código 0 |
| B0.T7 | — (verificación) | `git -C <worktree> check-ignore -v global-2026-padrones-completo.zip *.roster.json secrets/x` | Cada patrón resuelve contra una regla de `.gitignore` (código 0) |

**Criterios de salida**: worktree creado y activable, `dotnet build` y `npm ci` verdes dentro de
él, `orca.yaml` versionado, shim de CLI funcional, cero copias del zip fuente dentro del worktree.

**Riesgos**: el runtime de orca no está corriendo (falla toda delegación posterior) — mitigación:
verificar con `orca-cli worktree list --json` antes de T4. `--setup run` puede tardar por `npm ci`
en dos workspaces — sin mitigación adicional, es esperado.

**Rollback**: `orca-cli worktree remove --name plan-cope-cicd` (o eliminar el directorio de
worktree manualmente) y reintentar B0.T4 con `--setup skip` para diagnosticar si el script de
setup es la causa de la falla.

---

### Batch 1 — Persistencia y activación única por máquina

**Depende de**: Batch 0 (worktree operativo).
**Objetivo**: mover el estado persistente del Host fuera del directorio de instalación e
implementar la activación única por passphrase con DPAPI, sin tocar aún cifrado del padrón
(Batch 2) ni Velopack (Batch 6).

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B1.T1 | `src/Local/PlanCope.Local.Host/Services/DataDirectoryResolver.cs` (nuevo) | Servicio que resuelve `%LocalAppData%\PlanCope\{data,assets,config,logs}`, honra `PLANCOPE_DATA_DIR` si está seteada, y crea los subdirectorios si no existen | `dotnet test --filter DataDirectoryResolver` sale 0 |
| B1.T2 | `src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj`, puntos de uso de rutas de SQLite/assets/logs (a ubicar por lectura del código, no inventar paths) | Reemplazar rutas hardcodeadas junto al ejecutable por `DataDirectoryResolver` | `dotnet build src/Local/PlanCope.Local.Host` sale 0 |
| B1.T3 | `src/Local/PlanCope.Local.Host/Services/LegacyDatabaseMigrator.cs` (nuevo) | Al arrancar, si no existe base en el directorio persistente y sí existe una junto al ejecutable, copiarla al directorio persistente (una sola vez) | Test que crea una base "legada" en un temp dir, corre el migrador, verifica que aparece en el directorio persistente y que una segunda corrida no la sobreescribe |
| B1.T4 | `src/Local/PlanCope.Local.Host/Services/ActivationKeyStore.cs` (nuevo) | Servicio que protege/desprotege bytes vía `System.Security.Cryptography.ProtectedData` (DPAPI, `DataProtectionScope.CurrentUser`) | Test que protege y desprotege un array de bytes de prueba y verifica igualdad byte a byte |
| B1.T5 | `src/Local/PlanCope.Local.Host/ClientApp/src/host/activation/` (nueva carpeta), componente `ActivationScreen.tsx` | Pantalla que pide la passphrase una sola vez y no se vuelve a mostrar si ya hay clave protegida almacenada | `npm test -- activation` (o el runner de tests configurado en `ClientApp`) sale 0 |
| B1.T6 | `tests/PlanCope.Local.Api.Tests/` o proyecto de test equivalente para el Host | Test de migración: base legada + arranque = datos preservados y ubicados en el directorio nuevo | `dotnet test --filter DataMigration` sale 0 |

**Criterios de salida**: `dotnet build` + `dotnet test` verdes en toda la solución; una ejecución
manual del Host en un directorio limpio crea `%LocalAppData%\PlanCope\` con la estructura
esperada; una base copiada junto al ejecutable se migra una sola vez.

**Riesgos**: no se identificaron con certeza todos los puntos donde el código actual asume rutas
junto al ejecutable (B1.T2 requiere lectura exploratoria antes de escribir — delegar como
exploración, no como edición directa, si el primer intento de deepseek falla por no encontrar
todos los usos).

**Rollback**: revertir el commit del batch; el Host sigue funcionando con rutas junto al
ejecutable como antes (comportamiento previo, no roto).

---

### Batch 2 — Cripto del padrón

**Depende de**: Batch 1 (directorio de datos persistente ya resuelto, es donde vivirá el bundle
cifrado en runtime).
**Objetivo**: herramienta offline de empaquetado (zip de 1440 rosters → bundle cifrado) y el path
de runtime que descifra un único CUE bajo demanda.

**Precondición**: ninguna tarea de este batch lee el zip real de 227598 estudiantes. Todas usan
una fixture sintética de 2-3 CUEs con datos ficticios generada por B2.T1.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B2.T1 | `tools/PlanCope.RosterCrypto/Fixtures/` (nuevo) | Generar 2-3 archivos `<CUE>-2026.roster.json` sintéticos (datos ficticios, mismo shape que el real) para testing | `test -f tools/PlanCope.RosterCrypto/Fixtures/180000100-2026.roster.json` sale 0 |
| B2.T2 | `docs/roster-bundle-format.md` (nuevo) | Documentar el formato del bundle: header con magic bytes + versión, parámetros de Argon2id (memoria, iteraciones, paralelismo, salt), layout por entrada (CUE, nonce, tag GCM, DEK envuelta, ciphertext), y checksum por entrada | El archivo existe y contiene una tabla de campos con tamaño en bytes de cada uno |
| B2.T3 | `tools/PlanCope.RosterCrypto/EnvelopeEncryption.cs` (nuevo) | Implementar cifrado: generar DEK aleatoria de 256 bits por CUE, cifrar el JSON del roster con AES-256-GCM, envolver la DEK con una clave maestra derivada de una passphrase vía Argon2id | Test: cifrar la fixture de B2.T1, verificar que el ciphertext no contiene el string `"document"` en claro |
| B2.T4 | `tools/PlanCope.RosterCrypto/EnvelopeDecryption.cs` (nuevo) | Descifrar un único CUE del bundle dada la passphrase correcta | Test: cifrar con B2.T3 y descifrar con B2.T4 sobre la fixture, JSON resultante es byte-idéntico al original |
| B2.T5 | `tools/PlanCope.RosterCrypto/Program.cs` (nuevo, CLI) | CLI que toma un directorio de `.roster.json` + passphrase y produce un único archivo bundle cifrado + manifiesto | `dotnet run --project tools/PlanCope.RosterCrypto -- pack --input tools/PlanCope.RosterCrypto/Fixtures --output /tmp/test-bundle.enc --passphrase test-only` sale 0 y `/tmp/test-bundle.enc` existe |
| B2.T6 | Test negativo en `tools/PlanCope.RosterCrypto.Tests/` | Passphrase incorrecta debe fallar el descifrado sin producir datos parciales | Test que descifra con passphrase errónea y espera excepción/resultado de fallo explícito |
| B2.T7 | Test negativo en `tools/PlanCope.RosterCrypto.Tests/` | Bundle con un byte alterado en el ciphertext o en el tag GCM debe fallar la verificación de integridad | Test que corrompe un byte y espera fallo de autenticación (GCM tag mismatch) |
| B2.T8 | Test negativo en `tools/PlanCope.RosterCrypto.Tests/` | Pedir un CUE que no está en el manifiesto debe fallar con un error claro, no con excepción no controlada | Test que pide un CUE inexistente y espera una excepción de tipo específico documentado |
| B2.T9 | `src/Local/PlanCope.Local.Api/Services/EmbeddedRosterSeeder.cs` | Integrar el descifrado runtime: en vez de leer `.roster.json` embebidos en la DLL, leer el CUE solicitado del bundle cifrado vía `EnvelopeDecryption`, y pasar el JSON resultante al import existente (que ya pasa por `DocumentHmacService`) | `dotnet test --filter EmbeddedRosterSeeder` sale 0 |

**Criterios de salida**: cifrar+descifrar con fixtures pasa; los 3 tests negativos pasan; el
seeder de Local.Api importa desde el bundle cifrado en vez de recursos embebidos en la DLL; cero
referencias al zip real de 227598 estudiantes en el código o en tests versionados.

**Riesgos**: elegir parámetros de Argon2id (memoria/iteraciones) sin medir el hardware real de las
notebooks de campo — mitigación: dejar los parámetros configurables en el manifiesto del bundle,
no hardcodeados, y medir tiempo de derivación en Batch 6 durante las pruebas de instalación.

**Rollback**: el `EmbeddedRosterSeeder` puede volver temporalmente a leer `.roster.json` sueltos
(comportamiento de `docs/roster-release.md` actual) revirtiendo sólo B2.T9; el resto de la
herramienta de cripto queda disponible sin afectar el runtime.

---

### Batch 3 — Desacople autoría/toma

**Depende de**: Batch 0 (worktree). Independiente de Batch 1 y 2 (puede correr en paralelo si se
usa un worktree separado, pero se numera después para mantener el orden de riesgo creciente).
**Objetivo**: eliminar la autoría local de exámenes del `ClientApp`, dejando sólo Central Web como
autor y el Host como administrador de sesiones.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B3.T1 | — (verificación, no edita nada) | Ejecutar `find src/Local/PlanCope.Local.Host/ClientApp -name "*.test.*"` y listar cualquier test que referencie `exam-builder` | El comando sale 0; su salida se adjunta al reporte del batch antes de continuar con B3.T2 |
| B3.T2 | `src/Local/PlanCope.Local.Host/ClientApp/src/host/exam-builder/` (eliminar carpeta completa: `ExamBuilderPage.tsx`, `examTypes.ts`, `examSchema.ts`, `examBuilderUtils.ts`) | Eliminar el directorio completo | `test -d src/Local/PlanCope.Local.Host/ClientApp/src/host/exam-builder` sale distinto de 0 |
| B3.T3 | `src/Local/PlanCope.Local.Host/ClientApp/src/host/HostApp.tsx` | Quitar el import y el uso de `<ExamBuilderPage>` (línea 6 y 37 según el estado verificado) | `rg "ExamBuilderPage" src/Local/PlanCope.Local.Host/ClientApp/src` no encuentra coincidencias (o `grep` si `rg` no está disponible) |
| B3.T4 | `src/Local/PlanCope.Local.Host/ClientApp/src/host/api/apiClient.ts` | Quitar `importExam()` y el tipo `LocalExamJson` (líneas 8, 17-19 según el estado verificado) | `rg "importExam|LocalExamJson" src/Local/PlanCope.Local.Host/ClientApp/src` no encuentra coincidencias |
| B3.T5 | `src/Local/PlanCope.Local.Api/Endpoints/ExamEndpoints.cs` | Quitar únicamente el endpoint `POST /api/exams/import` (línea 30-37 según el estado verificado); conservar `GET /api/exams/`, `GET /api/exams/{id}/blocks` | `dotnet build src/Local/PlanCope.Local.Api` sale 0 y `rg "api/exams/import" src/Local/PlanCope.Local.Api` no encuentra coincidencias |
| B3.T6 | `src/Local/PlanCope.Local.Api/Services/LocalExamImportService.cs`, `Services/ImportLocalExamContracts.cs` (eliminar ambos archivos) | Eliminar los servicios exclusivos del import local | `dotnet build src/Local/PlanCope.Local.Api` sale 0 |
| B3.T7 | `src/Local/PlanCope.Local.Api/Data/LocalDataServiceCollectionExtensions.cs` | Quitar el registro de `LocalExamImportService` (línea 24 según el estado verificado) | `dotnet build src/Local/PlanCope.Local.Api` sale 0 |
| B3.T8 | `tests/PlanCope.Local.Api.Tests/LocalExamImportTests.cs` (eliminar archivo) | Eliminar el test del endpoint removido | `dotnet test tests/PlanCope.Local.Api.Tests` sale 0 (sin referencias rotas) |
| B3.T9 | — (verificación, no edita nada) | Test de integración existente que ejercite: pull de un examen publicado desde Central + listado en sesión + arranque de `useDeliverySession` | El test de sesión existente (identificar el archivo real durante la ejecución de esta tarea) sigue en verde tras B3.T2-B3.T8 |

**Criterios de salida**: `dotnet build` de toda la solución y `npm run build` de `ClientApp`
verdes; cero referencias a `exam-builder`, `importExam`, `LocalExamJson` en el árbol; los tests de
sesión (`student/*`, `SessionCreatePanel`, `useDeliverySession`) siguen pasando sin cambios.

**Riesgos**: B3.T1 puede revelar tests no enumerados que referencian `exam-builder` fuera de los 4
archivos conocidos — si aparecen, agregar tareas `B3.T2b`, `B3.T2c` según corresponda antes de
cerrar el batch, no ignorarlos.

**Rollback**: `git revert` del rango de commits de este batch; no hay migración de datos
involucrada (son sólo archivos de frontend/API sin estado persistente propio).

---

### Batch 4 — Docker y Compose para Coolify

**Depende de**: Batch 3 (Central API/Web deben compilar sin la dependencia del builder local sólo
si comparten contratos; en la práctica este batch es independiente de Batch 3 salvo por orden de
mezcla de ramas — declarar explícitamente si se ejecuta en paralelo).
**Objetivo**: contenerizar Central API y Central Web para consumo por Coolify.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B4.T1 | `src/Central/PlanCope.Central.Api/Dockerfile` (nuevo) | Multi-stage (`build` con SDK, `runtime` con ASP.NET runtime), usuario no root, `LABEL org.opencontainers.image.source`, `.revision`, `.version` | `docker build -f src/Central/PlanCope.Central.Api/Dockerfile -t plan-cope-central-api:test .` sale 0 |
| B4.T2 | `src/Central/PlanCope.Central.Web/Dockerfile` (nuevo) | Multi-stage (`deps`/`build` con Node, `runtime` con salida `standalone` de Next.js), usuario no root, mismas labels OCI | `docker build -f src/Central/PlanCope.Central.Web/Dockerfile -t plan-cope-central-web:test .` sale 0 |
| B4.T3 | `src/Central/PlanCope.Central.Web/next.config.*` | Confirmar/activar `output: "standalone"` | `rg "output.*standalone" src/Central/PlanCope.Central.Web/next.config.*` encuentra coincidencia |
| B4.T4 | `deploy/compose.dev.yml` (nuevo) | `postgres`, `migrate`, `api`, `web` para desarrollo local, con puertos publicados | `docker compose -f deploy/compose.dev.yml config` sale 0 |
| B4.T5 | `deploy/compose.ci.yml` (nuevo) | Igual composición sin puertos publicados en `postgres`; `${VAR:?}` en toda variable sensible (`POSTGRES_PASSWORD`, `ConnectionStrings__CentralDatabase`, `Auth__SigningKey`, `GeApi__Username`, `GeApi__Password`) | `docker compose -f deploy/compose.ci.yml config` sale distinto de 0 si falta una variable requerida, y 0 si todas están seteadas en el entorno de prueba |
| B4.T6 | `deploy/compose.ci.yml`, servicio `postgres` | Agregar volumen nombrado persistente + healthcheck (`pg_isready`) | `docker compose -f deploy/compose.ci.yml config` muestra el volumen y el healthcheck |
| B4.T7 | `deploy/compose.ci.yml`, servicio `migrate` | Servicio de una sola ejecución (`restart: "no"`) que corre un EF Core migration bundle contra `postgres`, y `api` depende de `migrate` con `condition: service_completed_successfully` | `docker compose -f deploy/compose.ci.yml config` valida sin error de dependencia circular |
| B4.T8 | `src/Central/PlanCope.Central.Api/Program.cs` | Agregar `UseForwardedHeaders` y separar `/health/live` de `/health/ready` (ready devuelve 503 si Postgres no responde) | `curl -sf http://localhost:8080/health/live` sale 0 con Postgres apagado; `curl -sf http://localhost:8080/health/ready` sale distinto de 0 en el mismo escenario |
| B4.T9 | `deploy/` | Smoke test end-to-end: `docker compose -f deploy/compose.ci.yml up -d` con variables de prueba, esperar `healthy`, pegarle a `/health/ready` | Script de smoke test sale 0 |

**Criterios de salida**: ambas imágenes construyen; `docker compose config` válido en dev y en
modo Coolify; readiness responde 503 con Postgres caído y 200 cuando está arriba; ninguna variable
sensible tiene valor por defecto en el compose de Coolify.

**Riesgos**: la ruta real del healthcheck de Postgres/API puede diferir de lo asumido si `Program.cs`
ya define endpoints de health con otro nombre — verificar antes de B4.T8 y ajustar el path, no el
comportamiento.

**Rollback**: los Dockerfiles y compose son aditivos (no tocan código de aplicación salvo
B4.T3/B4.T8); revertir esos dos archivos deja el resto sin efecto.

---

### Batch 5 — CI (`ci.yml`)

**Depende de**: Batch 4 (los jobs de contenedores necesitan Dockerfiles y compose existentes).
**Objetivo**: pipeline de CI obligatoria en PR y push a `main`.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B5.T1 | `.github/workflows/ci.yml` (nuevo) | Job `dotnet` en `windows-latest`: restore, `dotnet build -warnaserror`, `dotnet test`, build de `PlanCope.Local.Host` (WinForms) | `act -j dotnet -W .github/workflows/ci.yml --list` reconoce el job (o, si `act` no está disponible, `yamllint .github/workflows/ci.yml` sale 0) |
| B5.T2 | `.github/workflows/ci.yml` | Job `js` en `ubuntu-latest`: `npm ci`, Vitest de ambos workspaces, `npm run build` de Central Web y `ClientApp` | Igual verificación de sintaxis que B5.T1 |
| B5.T3 | `.github/workflows/ci.yml` | Job `containers`: build de ambas imágenes sin push, `docker compose -f deploy/compose.ci.yml config`, levantar `migrate` + smoke test contra Postgres efímero (servicio de GitHub Actions o contenedor en el job) | Igual verificación de sintaxis; smoke test local reproducido con `docker compose -f deploy/compose.dev.yml up --abort-on-container-exit migrate` sale 0 |
| B5.T4 | `.github/workflows/ci.yml` | Job `security`: escaneo de dependencias (`dotnet list package --vulnerable`, `npm audit --audit-level=high`), escaneo de imágenes, generación de SBOM (`syft` o `anchore/sbom-action`), attestations (`actions/attest-build-provenance`) | Igual verificación de sintaxis |
| B5.T5 | `.github/workflows/ci.yml` | Fijar cada `uses:` externo por SHA completo, no por tag flotante | `rg "uses: [a-zA-Z0-9./_-]+@v" .github/workflows/ci.yml` no encuentra coincidencias (todas deben ser `@<sha40>`) |
| B5.T6 | `.github/workflows/ci.yml` | `permissions:` a nivel workflow con el mínimo privilegio (`contents: read` por defecto, `id-token: write` sólo en el job que lo necesite para attestations) | `rg "permissions:" .github/workflows/ci.yml` muestra un bloque explícito, no ausente |
| B5.T7 | — (comando `gh`, no archivo) | Configurar protección de rama en `main`: requerir los 4 jobs de `ci.yml` como checks obligatorios antes de merge | `gh api repos/GeronimoSerial/plan_cope/branches/main/protection --jq .required_status_checks.contexts` incluye los 4 nombres de job |

**Criterios de salida**: `ci.yml` válido, corre en un PR de prueba, los 4 jobs pasan, protección
de rama exige los 4 checks.

**Riesgos**: sin `act` instalado localmente, la validación de sintaxis se limita a `yamllint`; la
validación real ocurre recién al abrir el primer PR de prueba — dejar esa verificación como parte
del criterio de salida, no asumirla completada sólo con lint.

**Rollback**: revertir `ci.yml`; sin protección de rama activa, PRs siguen mergeables (documentar
riesgo si el rollback ocurre después de B5.T7).

---

### Batch 6 — Velopack y firma

**Depende de**: Batch 1 (directorio de datos persistente, precondición explícita del plan
original) y Batch 2 (bundle cifrado del padrón debe poder empaquetarse junto al instalador).
**Objetivo**: empaquetar y firmar el Host con Velopack.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B6.T1 | `src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj` | Agregar referencia a `Velopack`, versión fijada explícitamente | `dotnet build src/Local/PlanCope.Local.Host` sale 0 |
| B6.T2 | `global.json` o script de CI (a determinar por lectura del repo) | Fijar la versión de la CLI `vpk` idéntica a la del paquete `Velopack` del csproj | Un script que compara ambas versiones y sale distinto de 0 si difieren, sale 0 cuando coinciden |
| B6.T3 | `src/Local/PlanCope.Local.Host/Program.cs` | `VelopackApp.Build().Run()` como primera instrucción de `Main`, antes de cualquier otra inicialización | `rg -A2 "static.*Main" src/Local/PlanCope.Local.Host/Program.cs` muestra `VelopackApp.Build().Run()` como primera línea del cuerpo |
| B6.T4 | `src/Local/PlanCope.Local.Host/Services/UpdateService.cs` (nuevo) | Chequeo de actualización no bloqueante, disparado después de que la ventana principal está operativa (no en el arranque) | Test que verifica que el chequeo no bloquea el hilo de UI (timeout artificial simulado) |
| B6.T5 | `src/Local/PlanCope.Local.Host/ClientApp/src/host/` | Acción visible "Buscar actualizaciones" + versión instalada mostrada en la UI | `npm run build` de `ClientApp` sale 0 y el componente nuevo tiene un test que renderiza la versión |
| B6.T6 | `src/Local/PlanCope.Local.Host/Services/UpdateService.cs` | Descarga en segundo plano + confirmación explícita antes de reiniciar para aplicar | Test que simula descarga completa y verifica que no se reinicia sin una confirmación explícita registrada |
| B6.T7 | `src/Local/PlanCope.Local.Host/Services/UpdateService.cs` | Soporte de canales `stable` y `beta` vía configuración | Test parametrizado que verifica que el canal se propaga a la llamada de Velopack |
| B6.T8 | `src/Local/PlanCope.Local.Host/Services/WebView2RuntimeCheck.cs` (nuevo) | Detectar Evergreen WebView2 Runtime instalado; si falta, disparar instalación | Test que simula ausencia del runtime y verifica que se invoca el instalador |
| B6.T9 | `.github/workflows/release.yml` (placeholder de esta sección; el workflow completo es Batch 7) o script local `scripts/sign-installer.ps1` (nuevo) | Firmar el ejecutable e instalador con el PFX (decisión 7), leyendo `WINDOWS_SIGNING_PFX`/`WINDOWS_SIGNING_PASSWORD` sólo desde variables de entorno | Script firma un ejecutable de prueba y `signtool verify /pa` sobre el binario firmado sale 0 |
| B6.T10 | — (procedimiento manual documentado, no automatizable por deepseek) | Matriz de pruebas: instalación limpia, actualización `N-1 -> N`, verificación de que datos persistentes sobreviven | Checklist en `docs/velopack-test-matrix.md` (nuevo) con los 3 escenarios y su resultado esperado documentado |

**Criterios de salida**: build firma sin error; `vpk` y el SDK de Velopack en la misma versión;
verificación manual (B6.T10) de instalación limpia y actualización sin pérdida de datos.

**Riesgos**: SmartScreen mostrará advertencia en el primer arranque con PFX autofirmado (decisión
7, esperado); documentar el paso "Más información → Ejecutar de todas formas" en las instrucciones
para Planeamiento, no como bug sino como consecuencia conocida.

**Rollback**: si la firma falla en CI, publicar sin firmar sólo en canal `beta` interno, nunca en
`stable`; no se relaja este control para producción.

---

### Batch 7 — Release, Coolify y portal de descarga

**Depende de**: Batch 4 (imágenes/compose), Batch 5 (CI en verde como gate previo), Batch 6
(Velopack firmado). Es el último batch: cierra el criterio de finalización de la sección 1.

| Tarea | Archivos objetivo | Cambio único | Aceptación |
|-------|--------------------|--------------|------------|
| B7.T1 | `.github/workflows/release.yml` (nuevo) | `workflow_dispatch` con inputs `version` (string), `channel` (`stable`\|`beta`), `target` (`staging`\|`production`) | `yamllint .github/workflows/release.yml` sale 0 |
| B7.T2 | `.github/workflows/release.yml` | Job de validación: SemVer válido y tag no existente (`git tag -l "$VERSION"` vacío) | Test local del script de validación con un `version` repetido sale distinto de 0 |
| B7.T3 | `.github/workflows/release.yml` | Build y push de ambas imágenes con tags `X.Y.Z`, `X.Y`, `sha-<commit>` | Tras un run de prueba en un registry de test, `docker manifest inspect` de los 3 tags sale 0 |
| B7.T4 | `.github/workflows/release.yml` | Mover `stable`/`latest` sólo después de confirmar que ambos tags inmutables existen en GHCR | Script que verifica orden de pasos: `stable`/`latest` no aparecen antes del paso de verificación en el YAML (revisión de orden de jobs/steps) |
| B7.T5 | `.github/workflows/release.yml` | Generar SBOM, checksums (`sha256sum`) y attestations para ambas imágenes | Artifacts del run de prueba incluyen `*.sbom.json` y `*.sha256` |
| B7.T6 | `.github/workflows/release.yml` | Job Host en `windows-latest`: build, descarga del release Velopack anterior para generar delta, empaquetar y firmar (reutiliza B6.T9) | Run de prueba produce `full`, `delta` e instalador en el workspace del job |
| B7.T7 | `.github/workflows/release.yml` | Disparo de deploy a Coolify (webhook) después de que ambas imágenes estén publicadas, con espera activa y timeout | Script de espera sale 0 cuando el webhook de prueba responde `200`, y sale distinto de 0 tras timeout simulado |
| B7.T8 | `.github/workflows/release.yml` | Smoke tests externos post-deploy contra la URL pública de Coolify | `curl -sf https://<host-de-prueba>/health/ready` sale 0 antes de continuar |
| B7.T9 | `.github/workflows/release.yml` | Publicar el GitHub Release (sólo metadata + SBOM + checksums, **nunca** el instalador con padrón, ver decisión 6) sólo si B7.T8 fue exitoso | `gh release view <tag> --json assets` no incluye ningún `.exe`/`.msi`/instalador Velopack |
| B7.T10 | Destino privado a definir (ver Preguntas abiertas #2); script `scripts/publish-private-installer.ps1` (nuevo) | Subir el instalador con padrón embebido al almacenamiento privado elegido | El script sale 0 y produce una URL/referencia verificable de forma privada (no pública) |
| B7.T11 | `src/Central/PlanCope.Central.Api/Data/Configurations/` (nueva configuración), migración EF Core en `src/Central/PlanCope.Central.Migrations` | Crear tabla `user_schools` (usuario ↔ CUE, N:N) **y** el rol de alcance provincial que la excede (ver decisión 13). No usar filas comodín: el alcance provincial se modela como rol, no como dato en `user_schools` | `dotnet ef migrations list --project src/Central/PlanCope.Central.Migrations` incluye la migración nueva y `dotnet ef database update` (contra Postgres de prueba) sale 0 |
| B7.T12 | `src/Central/PlanCope.Central.Api/Controllers/AuthController.cs`, `Auth/TokenService.cs` | Reemplazar `SchoolId: null` (líneas 31, 74 según el estado verificado) por el alcance real, y emitir un claim `roster_scope` con valor `province` o `school`. Para `school`, emitir además los CUEs asignados; para `province`, `school_id` queda nulo y el alcance lo da el rol | Test de integración: login de un usuario de Planeamiento devuelve `roster_scope=province`; login de un usuario de escuela devuelve `roster_scope=school` con al menos un CUE |
| B7.T13 | `src/Central/PlanCope.Central.Api/Controllers/RostersController.cs`, `RosterSyncController.cs`, política de autorización nueva | Reemplazar el `[Authorize]` de clase por una política por CUE: `roster_scope=province` permite cualquier CUE; `roster_scope=school` exige una fila coincidente en `user_schools` para el CUE solicitado. Denegar por defecto ante ausencia del claim | Tests: (a) usuario de escuela pidiendo un CUE ajeno recibe `403`; (b) usuario de escuela pidiendo su CUE recibe `200`; (c) usuario de Planeamiento recibe `200` para cualquier CUE; (d) token sin claim `roster_scope` recibe `403` |
| B7.T14 | `src/Central/PlanCope.Central.Api/Program.cs` | Registrar políticas de autorización por rol en `AddAuthorization()` (hoy vacío) y aplicar `[Authorize(Roles=...)]` donde corresponda | Test: usuario sin el rol requerido recibe `403` en un endpoint protegido de prueba |
| B7.T15 | `src/Central/PlanCope.Central.Api/Controllers/DownloadController.cs` (nuevo), `src/Central/PlanCope.Central.Web/app/descargas/` (nuevo) | Endpoint autenticado que sirve el instalador desde el almacenamiento privado de B7.T10, y página `/descargas` que lo consume vía el proxy BFF existente (`app/api/central/[...path]/route.ts`) | Request sin sesión a `/descargas` redirige a login (o responde 401/302 según el patrón de `app/(app)/layout.tsx`); con sesión válida, descarga inicia con `200` |
| B7.T16 | Documentación operativa: `docs/activation-passphrase.md` (nuevo) | Procedimiento de emisión y rotación de la passphrase de activación por release | El archivo existe y describe emisión inicial + rotación; sin script ejecutable si la política aún no está definida (ver Preguntas abiertas #4) |

**Criterios de salida**: ejecutar `release.yml` de punta a punta produce imágenes en GHCR, Coolify
saludable, GitHub Release sin instalador adjunto, instalador disponible sólo vía `/descargas`
autenticado, y las 3 brechas de autorización (B7.T12-B7.T14) cerradas con test que las verifica en
rojo antes del fix y en verde después.

**Riesgos**: B7.T10 depende de una decisión de almacenamiento no tomada (Preguntas abiertas #2);
no ejecutar B7.T10-B7.T15 hasta resolver esa pregunta con el usuario. B7.T16 depende de la
política de rotación (Preguntas abiertas #4).

**Rollback**: Coolify permite volver a la imagen anterior por tag/digest inmutable (sección 8); si
`release.yml` falla después de publicar imágenes pero antes de disparar Coolify, no hay estado
inconsistente porque Coolify no fue tocado.

---

## 7. Secretos y configuración

| Secreto/variable | Dónde vive | Quién puede verlo |
|---|---|---|
| `COOLIFY_WEBHOOK` | GitHub Secrets | Mantenedores del repo con acceso a Settings > Secrets |
| `COOLIFY_TOKEN` | GitHub Secrets | Idem |
| `WINDOWS_SIGNING_PFX` | GitHub Secrets (base64) | Idem |
| `WINDOWS_SIGNING_PASSWORD` | GitHub Secrets | Idem |
| `POSTGRES_PASSWORD` | Variable de entorno Coolify (no en GitHub) | Administradores de Coolify |
| `ConnectionStrings__CentralDatabase` | Variable de entorno Coolify | Administradores de Coolify |
| `Auth__SigningKey` | Variable de entorno Coolify | Administradores de Coolify |
| `GeApi__Username` | Variable de entorno Coolify (nunca en build) | Administradores de Coolify |
| `GeApi__Password` | Variable de entorno Coolify (nunca en build) | Administradores de Coolify |
| Passphrase de empaquetado del padrón | Fuera de GitHub y Coolify; gestión manual por quien prepara el release (ver Preguntas abiertas #4) | Sólo quien ejecuta el empaquetado |

Las credenciales de Gestión Educativa (`GeApi__*`) no participan de ningún build ni workflow de
CI/CD; sólo existen como configuración de runtime de Central API en Coolify.

---

## 8. Rollback y operación

- Tags y digests de imágenes son inmutables (`X.Y.Z`, `sha-<commit>`); `stable`/`latest` son los
  únicos móviles y sólo se mueven tras confirmar ambos tags inmutables (Batch 7).
- Rollback de Coolify: apuntar el servicio a la imagen/digest anterior conocida y redisparar
  deploy; no requiere cambios de código.
- Migraciones EF Core son forward-only; cambios de esquema siguen estrategia expand/contract
  (agregar columna nullable → backfill → hacer NOT NULL en un release posterior, nunca en el
  mismo paso que borra la columna vieja).
- Backup del volumen de Postgres antes de cualquier migración marcada como destructiva
  (`DROP COLUMN`, `DROP TABLE`, cambio de tipo con pérdida de precisión).
- Rollback de escritorio: publicar el código anterior con un número de versión **superior** al
  actual; Velopack no soporta downgrade seguro, por lo que nunca se apunta una instalación hacia
  una versión numéricamente menor.
- Versión y commit visibles en `/health`, logs estructurados y pantalla de diagnóstico del Host,
  para poder correlacionar un incidente con el release exacto.

---

## 9. Preguntas abiertas

Estas preguntas deben responderse antes o durante el batch que las referencia. No se asumen
respuestas por defecto.

1. ~~**¿La aplicación/proyecto de Coolify para Plan Cope ya existe en `coolify.sistemas.mec.gob.ar`,
   y ya se emitió el token de API?**~~ **RESUELTA** — el proyecto, ambiente y servidor de Coolify
   para Plan Cope ya existen (uuids en decisión 15) y el secreto que dispara el deploy es
   `COOLIFY_DEPLOY_TOKEN` (referenciado por nombre en `release.yml`); su provisionamiento por
   parte del usuario es un paso pendiente separado, no bloqueante de la definición del workflow.
2. **¿Dónde vive exactamente el artefacto del instalador privado?** Opciones mencionadas por el
   usuario: repositorio privado de GitHub con su propio mecanismo de Releases, o un volumen/objeto
   accesible desde Coolify. Bloquea B7.T10 y B7.T15 (cómo Central autentica para recuperarlo:
   `GITHUB_TOKEN` con scope a ese repo privado, credencial de storage, o acceso directo a
   filesystem/volumen compartido con el contenedor de Central API).
3. ~~**¿Cómo se mapea un usuario a uno o varios CUEs?**~~ **RESUELTA** — los usuarios de la
   Dirección de Planeamiento acceden a los 1.440 CUEs. Ver decisión 13; incorporada a B7.T11,
   B7.T12 y B7.T13.
4. **¿Cuál es la política de rotación de la passphrase de activación por release?** ¿Se rota en
   cada release, sólo ante sospecha de compromiso, o nunca mientras no cambie el equipo de
   Planeamiento? Bloquea B7.T16 y el diseño operativo de emisión (quién la genera, por qué canal
   se entrega al operador, cómo se invalidan passphrases anteriores si rota).
5. **Parámetros exactos de Argon2id** (memoria, iteraciones, paralelismo) para el hardware real de
   las notebooks de campo — dejados configurables en el manifiesto (Batch 2) pero sin valor de
   producción fijado; requiere una medición real antes del primer release con padrón cifrado.
