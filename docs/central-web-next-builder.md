# Central Web Next.js y distribucion de examenes

PlanCope ahora incluye una app web central en `src/Central/PlanCope.Central.Web`.

## Rol de cada pieza

- `PlanCope.Central.Web`: UI Next.js para login, gestion de examenes, versiones, bloques y publicacion.
- `PlanCope.Central.Api`: backend .NET para auth, persistencia, publicacion y sync.
- `PlanCope.Local.Api`: nodo local offline-first que descarga paquetes publicados por pull.
- `PlanCope.Local.Host`: consola local que puede disparar la sincronizacion y recargar el catalogo.

## Comandos

Desde la raiz del repo:

```powershell
npm install
npm run central:web:dev
npm run central:web:build
dotnet build PlanCope.slnx
dotnet test PlanCope.slnx --no-build
```

## Configuracion local de sync

El nodo local lee estos valores desde `sync_state`:

- `central_url`: URL base de Central API.
- `node_id`: identificador del nodo local.
- `central_access_token`: JWT bearer para llamar a Central API.
- `last_exam_pull_cursor`: cursor de paquetes publicados, administrado automaticamente.

`POST /api/sync/pull-exams` intenta descargar paquetes publicados desde Central y luego el Host local puede recargar `GET /api/exams`.

## Flujo inicial

1. Iniciar Central API.
2. Iniciar Central Web con `npm run central:web:dev`.
3. Iniciar sesion desde Central Web.
4. Crear examen y version.
5. Guardar bloques en la version.
6. Publicar por materia, curso/grado y division opcional.
7. Configurar `sync_state` local.
8. Usar "Actualizar" en el Host local para ejecutar pull y ver el examen disponible.
