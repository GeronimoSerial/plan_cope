# Plan de mejora UI/UX — Plan Cope

## Norte de diseño

Interfaz institucional, sobria y moderna para personal escolar y estudiantes. La decisión rectora es reducir contenido simultáneo y dejar una acción principal clara por contexto. Antes de sumar UI se debe eliminar, resumir, agrupar o mover contenido a una vista secundaria.

### Dirección visual

- **Paleta:** bordó institucional como acción y foco, amarillo sólo como firma de marca, neutros claros para estructura, verde y rojo exclusivamente para estados.
- **Tipografía:** Segoe UI/system-ui por disponibilidad offline; jerarquía compacta, títulos contenidos y texto auxiliar breve.
- **Layout:** una columna principal orientada a la tarea y una zona secundaria sólo cuando aporta contexto inmediato.
- **Firma:** una barra institucional amarilla fina y constante; no compite con el contenido.
- **Reglas:** una acción primaria por panel, sin sombras decorativas, radios discretos, contraste AA, foco visible y responsive real.

## Fuente de verdad

Estados permitidos: `Pendiente`, `En curso`, `En revisión`, `Aprobado`, `Bloqueado`.

### Batch 1 — Auditoría y discovery

- **Objetivo:** identificar ruido, duplicaciones y problemas de jerarquía en los flujos visibles.
- **Alcance:** Central Local (operador, acceso escuela, alumno y creador) y Central Web.
- **Tareas:**
  - Inventariar pantallas, acciones y estados.
  - Detectar textos prescindibles, acciones competidoras y contenido técnico expuesto.
  - Definir prioridades por pantalla y registrar decisiones de simplificación.
- **Archivos/secciones:** `src/Local/.../ClientApp/src`, `src/Central/PlanCope.Central.Web/app`, estilos globales.
- **Criterios de aceptación:** existe un diagnóstico accionable; Central Local tiene flujo objetivo y prioridades explícitas; no se modifica comportamiento de negocio.
- **Estado:** Aprobado

### Batch 2 — Fundaciones y componentes globales de Central Local

- **Objetivo:** establecer una base visual compacta, consistente y accesible.
- **Alcance:** tokens, controles, botones, paneles, títulos, mensajes, encabezado y navegación local.
- **Tareas:**
  - Ajustar color, tipografía, espaciado, bordes, foco y estados.
  - Permitir jerarquías de botón claras y anchos según contexto.
  - Simplificar encabezado, pie y selector de modo.
  - Corregir responsive a 320, 768, 1024 y 1440 px.
- **Archivos/secciones:** `shared/shared.css`, `shared/ui.tsx`, `host/host.css`, componentes de layout.
- **Criterios de aceptación:** tokens semánticos consistentes; controles accesibles; sin ancho mínimo que provoque scroll horizontal; una acción principal distinguible; build TypeScript correcto.
- **Estado:** Aprobado

### Batch 3 — Central Local: acceso y espacio de trabajo

- **Objetivo:** mostrar sólo la información y acción necesarias para comenzar o continuar una toma.
- **Alcance:** acceso por CUE, creación, reanudación y sesión activa.
- **Tareas:**
  - Reducir texto y datos técnicos del acceso escolar.
  - Aplicar revelado progresivo: ocultar operación de sesión hasta que exista una activa.
  - Priorizar crear toma; agrupar reanudación como alternativa secundaria.
  - Resumir estados, progreso y próximos pasos de la sesión activa.
  - Mantener errores y vacíos orientados a una acción concreta.
- **Archivos/secciones:** `HostApp.tsx`, `SchoolGate.tsx`, `SessionsWorkspace.tsx`, `SessionCreatePanel.tsx`, `SessionResumePanel.tsx`, `ActiveSessionPanel.tsx`, `host.css`.
- **Criterios de aceptación:** no se muestran paneles inactivos con datos vacíos; el siguiente paso es inequívoco; textos son breves; flujos existentes siguen funcionando; build correcto.
- **Estado:** Aprobado

### Batch 4A — Creador local

- **Objetivo:** reducir la exposición de controles y detalles técnicos del creador local.
- **Alcance:** builder local.
- **Tareas:**
  - Reducir acciones simultáneas y texto auxiliar.
  - Mover JSON y utilidades de archivo a un bloque avanzado.
  - Ocultar identificadores técnicos y simplificar acciones de bloque.
  - Diferenciar mensajes de éxito y error.
- **Archivos/secciones:** `host/exam-builder`, estilos de builder en `host.css`.
- **Criterios de aceptación:** una acción primaria; JSON no visible por defecto; etiquetas comprensibles; responsive correcto; build correcto.
- **Estado:** Aprobado

### Batch 4B — Experiencia del alumno

- **Objetivo:** dejar visible sólo lo necesario en cada paso de acceso, respuesta y entrega.
- **Alcance:** ingreso, identidad, navegación de preguntas, progreso, entrega y confirmación.
- **Tareas:**
  - Reducir instrucciones, encabezado y pie.
  - Unificar tono, términos, botones y espaciado.
  - Eliminar progreso duplicado y simplificar confirmación.
  - Mejorar semántica de respuestas y responsive.
- **Archivos/secciones:** `student`, `student.css` y, sólo si es imprescindible, UI compartida.
- **Criterios de aceptación:** un solo resumen de progreso; acción primaria inequívoca; respuestas agrupadas semánticamente; sin texto técnico; build correcto.
- **Estado:** Aprobado

### Batch 5A — Central Web: fundaciones, dashboard y listado

- **Objetivo:** alinear navegación y vistas de entrada con el lenguaje institucional y la jerarquía de tareas.
- **Alcance:** login, shell, dashboard y listado de exámenes.
- **Tareas:**
  - Simplificar navegación, encabezados, login y copy.
  - Reemplazar métricas genéricas por estados accionables o eliminarlas.
  - Reducir cards, metadata y acciones redundantes.
  - Mantener patrones accesibles y responsive.
- **Archivos/secciones:** layout, UI global, login, dashboard, listado y `globals.css`.
- **Criterios de aceptación:** lenguaje y tokens coherentes; dashboard orientado a tareas; una acción primaria; build/tests correctos.
- **Estado:** Aprobado

### Batch 5B — Central Web: edición y publicación

- **Objetivo:** reducir decisiones simultáneas al crear, versionar, editar y publicar exámenes.
- **Alcance:** alta, detalle/versiones, builder y publicación.
- **Tareas:**
  - Acortar formularios, metadata y textos técnicos.
  - Dejar guardar/publicar como acciones contextuales principales.
  - Mover exportación y opciones secundarias fuera del foco.
  - Eliminar advertencias y vistas duplicadas; corregir semántica de tabs/dialog.
- **Archivos/secciones:** rutas y componentes de `exams`, componentes `builder` y estilos asociados.
- **Criterios de aceptación:** publicación y edición no compiten; no se muestra metadata técnica habitual; tabs/dialog accesibles; build/tests correctos.
- **Estado:** Aprobado

### Batch 6 — Revisión final y verificación

- **Objetivo:** detectar regresiones, inconsistencias y ruido residual antes de aprobar el trabajo.
- **Alcance:** todas las superficies modificadas.
- **Tareas:**
  - Revisar diffs y recorridos principales.
  - Verificar build, tests disponibles y errores de consola.
  - Revisar responsive, foco, contraste, estados vacíos/error y textos.
  - Ejecutar una última pasada de eliminación de elementos prescindibles.
- **Archivos/secciones:** proyecto completo afectado por los batches anteriores.
- **Criterios de aceptación:** builds y tests relevantes correctos; sin scroll horizontal accidental; foco visible; jerarquía consistente; el plan refleja el estado final y hallazgos pendientes.
- **Estado:** Aprobado

## Registro de decisiones y revisión

- 2026-09-09: se identifica Central Local como prioridad operativa. El flujo objetivo es: escuela → elegir crear o retomar → operar sesión activa. El creador de exámenes permanece como destino secundario.
- 2026-09-09: se preservan los cambios preexistentes de `.atl/`, ajenos a esta intervención.
- 2026-09-09 — Batch 1 aprobado: la auditoría verificó panel de sesión vacío siempre visible, repetición de datos de sección/padrón, dos caminos de reanudación con igual peso, estado técnico expuesto y `min-width` que rompe pantallas pequeñas. En builder local, JSON y cuatro acciones técnicas compiten con la edición. En alumno, progreso e instrucciones se repiten. En Central Web, dashboard y builder concentran cards, metadata y acciones redundantes.
- 2026-09-09 — Prioridad aprobada: Central Local sin sesión responde “crear o retomar”; con sesión responde “compartir y monitorear”. No se renderiza información vacía para anticipar un estado futuro.
- 2026-09-09 — Batch 2 aprobado tras revisión de diff y build: se eliminó el ancho mínimo global, las sombras decorativas de la consola, la información de infraestructura del pie y el peso equivalente del creador. Se incorporaron foco visible, radios sobrios y breakpoints 320/480/768/1024. El CSS específico de alumno se revisará en Batch 4.
- 2026-09-09 — Batch 3 aprobado tras revisión de diff, `git diff --check` y build: sin sesión no se renderizan código, enlace ni métricas vacías; crear es el foco y retomar queda plegado. Con sesión, compartir y monitorear ocupa todo el ancho y las acciones alternativas quedan secundarias. Se corrigieron textos y semántica de progreso sin tocar hooks ni API.
- 2026-09-09 — El Batch 4 se divide en 4A y 4B para mantener entregas pequeñas, independientes y verificables.
- 2026-09-09 — Batches 4A y 4B aprobados tras revisión de diffs, build y `diff --check`: las herramientas JSON/IDs del creador quedan plegadas; “Guardar en este equipo” es la acción principal; el alumno tiene un solo resumen de progreso, copy consistente y mejores relaciones ARIA.
- 2026-09-09 — Central Web se divide en 5A y 5B para separar superficies de entrada de los flujos complejos de edición/publicación.
- 2026-09-09 — Batch 5A aprobado: dashboard sin métricas genéricas, máximo tres borradores/recientes, CTA único, listados con metadata mínima, shell sin emojis y login sin gradiente ni sombras. Next build correcto y 13 tests Vitest aprobados.
- 2026-09-09 — Batch 5B aprobado: datos opcionales plegados, metadata de schema retirada, guardar/publicar contextualizados, exportación movida a vista previa y relaciones ARIA de tabs/dialog corregidas. Builds Central y Local correctos; 13 tests aprobados; `diff --check` limpio.
- 2026-09-09 — Batch 6 aprobado: la revisión independiente no encontró bloqueantes. Se corrigió el acceso a Guardar desde Publicar cuando existen cambios, el wrap de títulos largos a 320 px y dos mensajes visibles del alumno. Verificación final: ambos builds correctos, 13 tests aprobados y `diff --check` sin errores. `next-env.d.ts` generado por Next fue restaurado; cambios preexistentes de `.atl/` permanecen intactos.
