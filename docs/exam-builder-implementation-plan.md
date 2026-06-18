# Plan de implementacion del creador de examenes

Este documento define una implementacion por fases para el entorno de autoria de examenes de PlanCope. El objetivo inicial es generar archivos JSON compatibles con `docs/local-exam-format.md`; luego, el mismo builder debe poder evolucionar hacia guardado local, publicacion centralizada y trabajo colaborativo en la nube.

## Decision base

El builder sera una herramienta propia, JSON-first, implementada en React/Vite y alineada al contrato local existente. No se adopta un headless CMS en la primera etapa porque el valor principal no es administrar contenido generico, sino crear examenes estructurados con validaciones especificas, assets, claves de respuesta, orden de bloques y preview fiel al flujo del alumno.

Stack inicial recomendado:

- `react-hook-form` para estado de formularios, errores y arrays dinamicos.
- `zod` para schemas y validacion previa a exportar JSON.
- `@hookform/resolvers` para integrar Zod con React Hook Form.
- `@base-ui-components/react` para componentes accesibles sin estilo visual impuesto.
- `@dnd-kit/core` y `@dnd-kit/sortable` para reordenar bloques.
- CSS propio del proyecto para mantener identidad visual y evitar dependencia de un design system externo pesado.

## Principios de diseno

- El JSON exportado es la fuente de verdad de la fase inicial.
- El builder debe consumir e importar el mismo JSON que genera.
- Toda validacion importante debe ejecutarse antes de exportar.
- La UI debe reflejar el examen real que vera el alumno, no solo una forma administrativa.
- La estructura debe permitir cambiar la persistencia sin reescribir la experiencia de autoria.
- Los bloques deben ser extensibles sin romper examenes existentes.

## Modelo funcional inicial

El builder debe soportar los campos definidos por el formato local:

- Metadata del examen: `id`, `examCode`, `title`, `grade`, `division`, `subject`, `versionNumber`.
- Assets: `id`, `fileName`, `mimeType`, `contentBase64`.
- Bloques ordenados:
  - `text`
  - `image`
  - `multiple_choice`
  - `true_false`
  - `short_answer`
- Validacion por bloque: `required`.
- Clave de respuesta por bloque cuando corresponda: `correctAnswer`, `scoreValue`.

## Fase 0 - Preparacion tecnica

Objetivo: dejar el proyecto listo para implementar el builder sin mezclar responsabilidades con la toma del examen.

Entregables:

- Instalar dependencias del builder en `src/Local/PlanCope.Local.Host/ClientApp`.
- Crear carpeta funcional para autoria, por ejemplo `src/exam-builder`.
- Definir tipos TypeScript del JSON de importacion/exportacion.
- Definir schema Zod equivalente al formato local.
- Agregar helpers puros para:
  - generar IDs estables;
  - normalizar examenes importados;
  - validar referencias de assets;
  - convertir archivos de imagen a base64;
  - serializar JSON con formato estable.

Criterios de salida:

- `npm run build` compila.
- Existe un schema centralizado para validar examenes.
- Existe una funcion pura `buildExamJson` o equivalente que produce el JSON final.

## Fase 1 - MVP JSON-first

Objetivo: permitir crear un examen completo y exportarlo como JSON valido.

Entregables:

- Pantalla de metadata del examen.
- Lista editable de bloques.
- Alta, edicion, duplicado y eliminacion de bloques.
- Reordenamiento de bloques con dnd-kit.
- Formularios por tipo de bloque:
  - texto;
  - imagen;
  - opcion multiple;
  - verdadero/falso;
  - respuesta corta.
- Carga de imagenes y conversion a assets embebidos en base64.
- Validacion visible en UI antes de exportar.
- Exportacion de archivo `.json`.
- Importacion de un JSON existente para continuar editando.

Uso sugerido de Base UI:

- `Dialog` para crear o editar bloques.
- `Select` para elegir tipo de bloque, grado, division o materia cuando aplique.
- `Tabs` para alternar entre edicion, preview y JSON.
- `Accordion` para colapsar bloques.
- `Checkbox` para marcar preguntas obligatorias.
- `Switch` para activar o desactivar clave de respuesta.
- `Popover` o `Menu` para acciones secundarias de bloque.
- `Tooltip` para ayudas breves.

Criterios de salida:

- El JSON exportado se puede importar con `POST /api/exams/import`.
- El builder puede abrir y volver a guardar `docs/local-exam-format.json`.
- Se detectan errores basicos antes de exportar:
  - `examCode` y `title` vacios;
  - bloques sin ID;
  - imagenes que apuntan a assets inexistentes;
  - opcion multiple con menos de dos opciones;
  - valores de opcion duplicados;
  - clave de respuesta que no coincide con opciones disponibles;
  - asset sin `contentBase64` o con `mimeType` no permitido.

## Fase 2 - Preview fiel del examen

Objetivo: que el autor pueda revisar la experiencia del alumno antes de exportar.

Entregables:

- Vista previa del examen usando componentes compartidos o compatibles con la pantalla de alumno.
- Render de todos los tipos de bloque soportados.
- Indicacion visual de preguntas obligatorias.
- Simulacion de respuestas sin persistir.
- Panel de resumen:
  - cantidad de bloques;
  - cantidad de preguntas;
  - cantidad de obligatorias;
  - puntaje total si hay claves.

Criterios de salida:

- El preview muestra el mismo contenido que vera el alumno luego de importar el JSON.
- Los assets cargados se ven correctamente antes de exportar.
- La UI advierte si el examen no tiene preguntas o si todas las preguntas son opcionales.

## Fase 3 - Guardado local de borradores

Objetivo: permitir trabajar en examenes sin depender inmediatamente de archivos manuales.

Entregables:

- Persistencia de borradores en almacenamiento local del navegador o en SQLite via API local.
- Lista de borradores.
- Crear, abrir, renombrar, duplicar y eliminar borradores.
- Auto-guardado con estado visible.
- Historial minimo de cambios o snapshots manuales.

Opciones de persistencia:

- `localStorage` o `IndexedDB` para una version rapida y puramente frontend.
- SQLite local via endpoint de la API si se quiere integracion con el host desde temprano.

Decision recomendada:

- Empezar con `IndexedDB` si el objetivo sigue siendo velocidad.
- Migrar a API local cuando se necesiten usuarios, auditoria, backups o integracion con sincronizacion.

Criterios de salida:

- Cerrar y abrir el host conserva los borradores.
- Exportar desde un borrador produce el mismo JSON validado de la Fase 1.
- Importar un JSON permite guardarlo como nuevo borrador.

## Fase 4 - Integracion con API local

Objetivo: conectar el builder con el flujo real de importacion y prueba local.

Entregables:

- Boton "Importar en esta escuela" o equivalente que llame a `POST /api/exams/import`.
- Manejo de errores de importacion de la API.
- Confirmacion de examen importado con `examCode`, version y cantidad de bloques.
- Acceso rapido para probar el examen importado en el flujo local.

Criterios de salida:

- Un operador puede crear, validar, importar y probar un examen sin tocar archivos manualmente.
- Los errores de backend se muestran de forma clara y accionable.
- El builder sigue permitiendo exportar JSON para transporte offline.

## Fase 5 - Preparacion para nube

Objetivo: separar autoria, validacion y persistencia para que el builder pueda usar Central API mas adelante.

Entregables:

- Interfaz de repositorio de examenes en frontend:
  - `saveDraft`;
  - `loadDraft`;
  - `listDrafts`;
  - `publishVersion`;
  - `exportJson`.
- Adaptador local JSON/IndexedDB.
- Adaptador API local si corresponde.
- Diseno de endpoints futuros para Central:
  - crear examen;
  - crear version;
  - upsert de bloques;
  - subir assets;
  - publicar version;
  - listar historial.

Criterios de salida:

- La UI no depende directamente de una unica forma de almacenamiento.
- El JSON sigue siendo compatible con el formato local.
- Existe una ruta clara para publicar a escuelas cuando el modulo Central avance.

## Fase 6 - Flujo editorial central

Objetivo: convertir el builder en una herramienta de autoria institucional.

Entregables:

- Estados editoriales:
  - borrador;
  - en revision;
  - aprobado;
  - publicado;
  - archivado.
- Roles y permisos:
  - autor;
  - revisor;
  - administrador;
  - operador de escuela.
- Historial de versiones.
- Comparacion basica entre versiones.
- Comentarios o notas de revision.
- Publicacion a targets:
  - escuela;
  - curso;
  - materia;
  - periodo;
  - paquete offline.

Criterios de salida:

- Un examen puede ser creado, revisado y publicado desde Central sin romper la importacion local.
- Las escuelas reciben versiones inmutables y trazables.
- El builder conserva modo offline/exportable para contingencias.

## Backlog posterior

- Rich text controlado para consignas, con salida segura y portable.
- Soporte para formulas matematicas.
- Banco de preguntas reutilizable.
- Importacion desde CSV o planillas.
- Plantillas por materia o grado.
- Validacion pedagogica asistida.
- Previsualizacion de impresion/PDF.
- Rubricas para respuestas abiertas.
- Multi-idioma si el alcance institucional lo requiere.
- Adjuntos no imagen, si el formato de examen se amplifica.

## Riesgos y mitigaciones

| Riesgo | Mitigacion |
|---|---|
| El builder genera JSON que la API local no acepta | Compartir reglas de validacion, agregar tests de compatibilidad con ejemplos reales |
| La UI de bloques se vuelve dificil de mantener | Mantener un registro central de tipos de bloque y componentes por tipo |
| Las imagenes base64 agrandan demasiado los archivos | Validar tamano, comprimir cuando aplique y preparar almacenamiento externo futuro |
| Se mezclan autoria y toma del examen | Separar rutas, componentes y servicios de `exam-builder` y `student` |
| Cambios futuros rompen examenes existentes | Incluir `schemaVersion`, migradores y tests con fixtures historicos |

## Primer incremento recomendado

El primer incremento implementable deberia ser pequeno:

1. Agregar dependencias.
2. Crear tipos y schema Zod.
3. Crear pantalla de metadata.
4. Crear CRUD de bloques sin drag and drop.
5. Exportar JSON valido.
6. Importar `docs/local-exam-format.json`.
7. Validar con la API local.

Despues de eso, agregar reordenamiento, preview fiel y persistencia local.
