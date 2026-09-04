# Padrón nominal embebido por release

## Flujo

La consulta a GE pertenece a la preparación de un release, no a la operación en
la escuela:

1. Central ejecuta una actualización explícita por CUE y ciclo con
   `POST /api/rosters/refresh`. Esa es la única llamada que obtiene la nómina de GE.
2. Se revisan `sectionCount`, `studentCount` y el detalle de secciones en Central.
3. Se descarga el paquete aprobado desde
   `GET /api/sync/roster/{cue}/{schoolYear}`.
4. El JSON se guarda como
   `src/Local/PlanCope.Local.Api/Data/Rosters/<cue>-<ciclo>.roster.json`.
5. Se publica `PlanCope.Local.Host`. MSBuild incluye todos los `.roster.json`
   dentro de `PlanCope.Local.Api.dll`.
6. En el primer arranque, el nodo valida e importa los paquetes en SQLite. En
   arranques siguientes el checksum evita duplicarlos.

El operador local sólo escribe el CUE, selecciona el ciclo y lee el padrón local.
No existe una actualización de GE desde la pantalla de toma.

## Formato del CUE

La identidad canónica tiene 9 dígitos: 7 del establecimiento y 2 del anexo. Por
ejemplo, `1800554-00` se almacena y transporta como `180055400`; únicamente el
cliente de GE vuelve a colocar el guion al construir la consulta externa.

## Preparación automatizada

Con Central funcionando y una sesión autorizada, el siguiente comando ejecuta
una sola actualización de GE, descarga el paquete, lo embebe y publica el Host:

```powershell
$env:CENTRAL_ACCESS_TOKEN = "<token>"
./scripts/Build-SchoolRelease.ps1 `
  -Cue 180108600 `
  -SchoolYear 2026 `
  -CentralUrl https://central.example
```

El resultado queda en
`artifacts/local-release/180108600-2026/PlanCope.Local.Host.exe`. El padrón se
incluye como recurso de `PlanCope.Local.Api.dll`; el nodo no necesita conectarse
a Central ni a GE durante la toma.

Si Central no está disponible en la máquina de preparación, la herramienta
`tools/PlanCope.RosterReleaseTool` permite hacer el mismo corte directamente
contra GE. Lee `GE_API_USERNAME` y `GE_API_PASSWORD` sólo desde el entorno del
proceso, genera el paquete compatible y no persiste las credenciales ni el token.

Antes de publicar debe configurarse `Nominalization:DocumentHmacKey` con al menos
32 bytes. Si falta, el arranque con un padrón embebido falla deliberadamente: no
se acepta una importación donde los DNI no puedan protegerse con HMAC.

## Controles del release

- No publicar si Central informa cero alumnos o cero secciones.
- Conservar el JSON sólo en el circuito seguro de build: contiene información
  nominal antes de que el nodo la convierta a hashes.
- Verificar visualmente que el selector local enumera todas las secciones y que
  la suma de sus alumnos coincide con `studentCount`.
- Generar un release nuevo sólo cuando se aprueba un nuevo corte del padrón.
