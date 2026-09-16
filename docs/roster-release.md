# Padrón nominal embebido por release

## Flujo actual

El padrón de todas las escuelas viaja cifrado en un único bundle y el Host se
compila **una sola vez** para todas las escuelas. No existe un build por CUE ni
un `PlanCope.Local.Host.exe` distinto por escuela.

1. **Corte de GE.** `tools/PlanCope.RosterReleaseTool` consulta GE y escribe un
   paquete `<cue>-<ciclo>.roster.json` por CUE. En modo batch genera un archivo
   por CUE en un directorio de salida.
2. **Empaquetado cifrado.** `tools/PlanCope.RosterCrypto pack` toma ese
   directorio y produce un único bundle con **todos** los CUE, protegido por una
   passphrase compartida. El bundle es el único artefacto que contiene el padrón
   y viaja siempre cifrado.
3. **Build universal.** `PlanCope.Local.Host` se publica una sola vez
   (win-x64, self-contained) y ese mismo binario se distribuye a todas las
   escuelas. El roster no se embebe en la DLL.
4. **Fase A (offline).** En el primer arranque el operador elige su CUE e
   ingresa la passphrase. El nodo descifra **sólo** la entrada de ese CUE desde
   el bundle y la importa a SQLite. No hay llamada a Central ni a GE.

La consulta a GE pertenece a la preparación del release, no a la operación en la
escuela. Una vez completada la Fase A, el nodo trabaja con el padrón local.

## Corte de GE (generación de los `.roster.json`)

La herramienta `tools/PlanCope.RosterReleaseTool` genera el paquete compatible
directamente contra GE, sin pasar por Central:

```text
PlanCope.RosterReleaseTool <cue> <ciclo> <archivo-salida> [--inspect-schema]
PlanCope.RosterReleaseTool <archivo-de-cues> <ciclo> <directorio-salida> [--retry-failed]
```

- Si el primer argumento es un archivo existente, se ejecuta en **modo batch**:
  lee un CUE por línea, descarga cada padrón y escribe
  `<cue>-<ciclo>.roster.json` en el directorio de salida, más un resumen
  `sync-summary-<ciclo>.json`. `--retry-failed` reintenta sólo los CUE que
  fallaron en la corrida anterior.
- En modo CUE único escribe el archivo indicado. `--inspect-schema` imprime el
  esquema de la respuesta de GE y no genera paquete.
- Las credenciales se leen de `GE_API_USERNAME` y `GE_API_PASSWORD`; si faltan,
  se piden por entrada estándar. No se persisten en disco.
- Un corte vacío no produce paquete (salida `4`). En batch, un CUE sin padrón
  utilizable se marca como vacío y no aborta la corrida; los fallos reales se
  listan en el resumen y la salida es `5`.

## Empaquetado cifrado del bundle

```text
dotnet run --project tools/PlanCope.RosterCrypto -- pack \
  --input <directorio-con-roster-json> \
  --output <bundle.enc> \
  --passphrase <valor> \
  [--memory-kib N --iterations N --parallelism N]
```

- Toma todos los `*.roster.json` del directorio de entrada (sólo el nivel
  superior), ordenados por nombre. Cada archivo debe empezar con el CUE de 9
  dígitos seguido de `-`; no se admiten dos entradas para el mismo CUE.
- Deriva una clave maestra (KEK) de 256 bits desde la passphrase con Argon2id y
  un salt aleatorio de 16 bytes por bundle. Cada entrada usa una DEK aleatoria
  de 256 bits: el JSON se cifra con AES-256-GCM y la DEK se envuelve con la KEK
  (también AES-256-GCM). El AAD ata magic, versión, CUE y checksum SHA-256.
- Al leer, el nodo recorre los metadatos y saltea los ciphertexts hasta
  encontrar el CUE pedido: descifra **sólo** esa entrada. Un CUE ausente falla
  con `RosterEntryNotFoundException`.
- El CLI emite además `<bundle>.manifest.json` con versión, parámetros de
  Argon2id, CUE, offset, longitud y checksum; nunca incluye documentos ni
  nombres. El formato binario completo está en
  [`docs/roster-bundle-format.md`](roster-bundle-format.md).
- Los valores por defecto de Argon2id son 19 MiB, 2 iteraciones y paralelismo 1;
  son configurables y quedan guardados en el header del bundle.

## Build universal del Host

```text
dotnet publish src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj \
  -c Release -r win-x64 --self-contained true -o <directorio>
```

El job `host` de `.github/workflows/release.yml` hace exactamente este publish,
firma el ejecutable y lo empaqueta con Velopack. El binario es idéntico para
todas las escuelas: no recibe `RosterBundlePath` ni ningún parámetro por CUE.

El bundle cifrado no se embebe en el binario. Se distribuye junto al release y el
nodo lo ubica con `RosterBundle:Path` (variable de entorno `RosterBundle__Path`).
El instalador que lleva el padrón cifrado nunca se publica como asset público de
GitHub (decisión 6).

## Fase A: activación offline

En el primer arranque, el nodo todavía no tiene padrón importado. La activación
es completamente offline:

1. `GET /api/activation/bundle-cues` lista los CUE disponibles leyendo el header
   del bundle, sin necesitar la passphrase.
2. `POST /api/activation/unlock` con `{ "passphrase": "...", "cue": "180055400" }`
   descifra únicamente la entrada de ese CUE, la valida y la importa a SQLite.
   Una passphrase o CUE inválidos devuelven `400`.
3. La importación pasa por `DocumentHmacService`, que exige
   `Nominalization:DocumentHmacKey`. Al completar, el nodo registra su
   `NodeIdentity` con el CUE y el fingerprint de hardware.
4. `GET /api/activation/status` devuelve `{ "phaseAComplete": true, "cue": ... }`.

El seeder no importa nada en el arranque: la importación del padrón ocurre
siempre en el unlock de Fase A. La passphrase se ingresa en ese momento y nunca
se guarda en archivos versionados.

## Formato del CUE

La identidad canónica tiene 9 dígitos: 7 del establecimiento y 2 del anexo. Por
ejemplo, `1800554-00` se almacena y transporta como `180055400`; únicamente el
cliente de GE vuelve a colocar el guion al construir la consulta externa.

## Requisito de la clave HMAC

Antes de publicar debe configurarse `Nominalization:DocumentHmacKey` con al menos
32 bytes UTF-8. Si falta, la importación del padrón falla deliberadamente en la
Fase A: no se acepta un padrón cuyos DNI no puedan protegerse con HMAC.

## Controles del release

- No publicar un corte de GE con cero alumnos o cero secciones.
- Conservar el JSON nominal (`*.roster.json`) sólo en el circuito seguro de
  build: contiene información nominal antes de que el nodo la convierta a
  hashes. Nada derivado del padrón (JSON crudo, bundle cifrado, claves,
  manifiestos) se sube a artefactos públicos.
- La passphrase del bundle no se versiona ni se comparte fuera del operador.
- Verificar que `GET /api/activation/bundle-cues` enumera todos los CUE
  esperados y que la suma de alumnos del corte coincide con `studentCount`.
- Generar un release nuevo sólo cuando se aprueba un nuevo corte del padrón.
