# Plan de CI/CD, Coolify y actualizaciones de Plan Cope

## Objetivo

Usar GitHub como punto único para validar y lanzar versiones de Plan Cope:

1. Cada pull request ejecuta compilación, pruebas y validaciones de contenedores.
2. Un workflow manual de GitHub construye una versión identificada por SemVer.
3. Las imágenes de Central API y Central Web se publican en GHCR.
4. GitHub Actions dispara el despliegue de las imágenes verificadas en Coolify.
5. El Host Windows se empaqueta con Velopack y se publica en GitHub Releases.
6. Las instalaciones existentes detectan, descargan y aplican actualizaciones sin perder datos locales.

## Arquitectura del release

```text
Pull Request -> CI obligatoria
                       |
GitHub Run workflow -> Release versionado
                       |-- Central API/Web -> GHCR -> Coolify
                       `-- Host Windows -> Velopack -> GitHub Release
```

GitHub Actions construye los artefactos. Coolify no compila el repositorio: consume imágenes ya publicadas y verificadas.

## Tratamiento transitorio del padrón

Para el piloto se utilizará el snapshot ya sincronizado:

- CUE: `180108600`
- Ciclo lectivo: `2026`
- Secciones: `18`
- Estudiantes: `240`

La base se entrega por un canal privado y no se incorpora al repositorio, GHCR ni GitHub Releases. El ZIP de handoff incluye una copia saneada de SQLite y la clave HMAC necesaria para ese snapshot.

Antes de habilitar Velopack, los datos persistentes se moverán fuera del directorio de instalación:

```text
%LocalAppData%\PlanCope\
  data\plan-cope-local.db
  assets\
  config\
  logs\
```

Se agregará `PLANCOPE_DATA_DIR` para instalaciones administradas. La aplicación migrará una base antigua encontrada junto al ejecutable sólo cuando todavía no exista una base en el directorio persistente. Las actualizaciones reemplazarán binarios y frontend, nunca SQLite, assets, configuración o claves.

## Actualización nocturna futura del padrón

La computadora escolar no necesita permanecer encendida de noche. El proceso recomendado es:

```text
Gestión Educativa
        |
job nocturno en Central
        |
snapshot versionado en PostgreSQL
        |
pull local al iniciar o recuperar conectividad
        |
snapshot activo en SQLite
```

Central consultará Gestión Educativa mediante un job programado, generará snapshots inmutables por CUE y ciclo, y conservará auditoría, checksum y estado. La app local preguntará por una versión nueva al iniciar y periódicamente mientras tenga conectividad.

La importación local será transaccional: descargar, validar CUE, ciclo, checksum y cantidades, importar en staging y cambiar el snapshot activo sólo después de una validación completa. Ante cualquier error se conserva el padrón anterior. Se añadirá actualización manual, fecha visible del último padrón exitoso y alertas por antigüedad.

## Fase 1: persistencia y preparación del Host

1. Introducir un servicio único que resuelva el directorio persistente.
2. Mover SQLite, assets, configuración y logs a ese directorio.
3. Generar una clave HMAC distinta por instalación y protegerla mediante Windows DPAPI.
4. Separar el aprovisionamiento del padrón del instalador público.
5. Convertir `Build-SchoolRelease.ps1` en una herramienta privada de aprovisionamiento, no en un release binario por escuela.
6. Agregar pruebas de migración desde la disposición actual y de conservación de datos.

## Fase 2: integración de Velopack

1. Referenciar Velopack desde `PlanCope.Local.Host` y fijar la misma versión para el SDK y `vpk`.
2. Ejecutar `VelopackApp.Build().Run()` como primera instrucción de `Main`.
3. Publicar `win-x64` como aplicación self-contained.
4. Implementar un servicio de actualización que no bloquee el arranque offline.
5. Buscar actualizaciones después de que la ventana esté operativa.
6. Agregar una acción visible para buscar actualizaciones y mostrar la versión instalada.
7. Descargar en segundo plano y pedir confirmación antes de reiniciar.
8. Comenzar con canales `stable` y `beta`.
9. Detectar o instalar Evergreen WebView2 Runtime.
10. Probar instalación limpia, actualización `N-1 -> N` y conservación de la base.

## Fase 3: Docker y Compose

Crear dos imágenes independientes:

- `ghcr.io/geronimoserial/plan-cope-central-api`
- `ghcr.io/geronimoserial/plan-cope-central-web`

Archivos previstos:

```text
src/Central/PlanCope.Central.Api/Dockerfile
src/Central/PlanCope.Central.Web/Dockerfile
deploy/compose.dev.yml
deploy/compose.coolify.yml
```

Los Dockerfiles serán multi-stage, usarán un usuario sin privilegios e incluirán etiquetas OCI con repositorio, versión y commit.

El Compose contendrá:

- `postgres`: PostgreSQL 16 con volumen persistente y healthcheck.
- `migrate`: servicio de una ejecución con un EF Core migration bundle.
- `api`: ASP.NET Core, iniciado después de una migración exitosa.
- `web`: salida standalone de Next.js, conectada internamente a `http://api:8080`.

PostgreSQL no publicará puertos. Web y API tendrán dominios administrados por Coolify. ASP.NET Core configurará forwarded headers. Se separarán liveness y readiness; readiness devolverá `503` cuando PostgreSQL no esté disponible.

Las variables sensibles serán obligatorias mediante `${VARIABLE:?}` y vivirán únicamente en Coolify. Se configurarán backups automáticos del volumen PostgreSQL.

## Fase 4: integración continua

Crear `.github/workflows/ci.yml` para pull requests y pushes a `main`.

Jobs previstos:

1. .NET en Windows: restore, build con warnings como errores, tests y build del Host WinForms.
2. JavaScript en Linux: `npm ci`, Vitest, Central Web y Local UI.
3. Contenedores: build sin push, `docker compose config`, migración y smoke tests con PostgreSQL efímero.
4. Seguridad: escaneo de dependencias e imágenes, SBOM y artifact attestations.

Las acciones externas se fijarán por SHA y los permisos de `GITHUB_TOKEN` seguirán el principio de mínimo privilegio. `main` requerirá todos los checks antes de aceptar un merge.

## Fase 5: release desde GitHub

Crear `.github/workflows/release.yml` con `workflow_dispatch` y estas entradas:

```text
version: 1.2.0
channel: stable | beta
target: staging | production
```

Secuencia:

1. Validar SemVer y que el tag no exista.
2. Ejecutar nuevamente los controles críticos.
3. Crear el tag y un GitHub Release draft.
4. Construir las dos imágenes y publicarlas con tags de versión, minor, commit y canal.
5. Generar SBOM, checksums y attestations.
6. Construir el Host en `windows-latest`.
7. Descargar el release Velopack anterior para generar deltas.
8. Empaquetar y firmar ejecutables e instalador.
9. Subir full package, delta, instalador y manifiesto al Release draft.
10. Disparar Coolify después de publicar ambas imágenes.
11. Esperar el despliegue y ejecutar smoke tests externos.
12. Publicar el GitHub Release únicamente cuando producción esté saludable.

Tags de imágenes:

```text
1.2.0
1.2
sha-<commit>
stable
latest
```

Los tags `stable` y `latest` se moverán solamente después de que ambas imágenes inmutables estén disponibles.

## Fase 6: seguridad y firma

Secretos de GitHub:

```text
COOLIFY_WEBHOOK
COOLIFY_TOKEN
WINDOWS_SIGNING_*
```

Secretos exclusivos de Coolify:

```text
POSTGRES_PASSWORD
ConnectionStrings__CentralDatabase
Auth__SigningKey
GeApi__Username
GeApi__Password
```

Las credenciales de Gestión Educativa no participarán del build. Para evitar advertencias de SmartScreen, el instalador y los ejecutables se firmarán con Azure Trusted Signing o un certificado equivalente. Un PFX cifrado en GitHub Secrets puede utilizarse sólo como primera etapa.

GitHub Releases nunca contendrá bases SQLite, padrones, credenciales ni claves HMAC de instalaciones.

## Fase 7: rollback y operación

- Conservar tags y digests inmutables de las imágenes.
- Permitir que Coolify vuelva a una versión anterior conocida.
- Utilizar migraciones forward-only y estrategia expand/contract.
- Realizar un backup antes de cualquier migración destructiva.
- Para revertir desktop, publicar el código anterior con un número de versión superior; no intentar un downgrade de Velopack.
- Mostrar versión y commit en health, logs y pantalla de diagnóstico.

## Entregas sugeridas

1. Persistencia segura y uso del padrón actual.
2. Dockerfiles, migration bundle y Compose.
3. CI y protección de `main`.
4. Velopack y experiencia de actualización.
5. Release workflow, GHCR y Coolify.
6. Firma, smoke tests, documentación y simulacro de rollback.
7. Job nocturno Central -> Gestión Educativa y pull local resiliente.

## Criterio de finalización

Desde GitHub debe ser posible ejecutar un workflow indicando una versión y obtener automáticamente:

- dos imágenes verificadas en GHCR;
- Central desplegado y saludable en Coolify;
- un GitHub Release publicado con instalador y paquetes Velopack;
- una actualización ofrecida a una instalación de la versión anterior;
- SQLite, padrón, assets y configuración intactos después de actualizar.
