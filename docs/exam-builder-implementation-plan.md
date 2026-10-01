# Plan de implementación del creador de exámenes

Este documento describe el modelo vigente y el trabajo pendiente del creador de exámenes de
PlanCope. La autoría se realiza en Central y las versiones publicadas se distribuyen a los nodos
locales mediante sincronización. El contrato de la API está en
`docs/central/exam-publishing-contract.md`; los ejemplos portables están en
`docs/local-exam-format.md`.

## Modelo vigente

- Metadata: `code`, `title`, `description`, `courses`, `area` y `subject`. Los cursos usan claves
  como `primaria-6` o `secundaria-1`.
- Preguntas ordenadas: `multiple_choice` y `true_false`.
- Una pregunta puede incluir `config.imageAssetId` para mostrar una imagen junto al enunciado.
- Las imágenes son assets JPEG, PNG o WebP de hasta 2 MiB cada una. Solo se publican assets
  referenciados por alguna pregunta.
- Una pregunta `multiple_choice` usa `config.multiple` para permitir una o varias respuestas. Si
  `multiple` es `true`, puede incluir `config.scoringPolicy`: `AllOrNothing`,
  `ProportionalPenalised` o `ProportionalPlain`. Si se omite, se aplica `AllOrNothing`.
- Cada pregunta puede marcarse como obligatoria y tener su clave de respuesta y puntaje.

El enunciado de cada pregunta contiene el texto necesario para resolverla. No se crean bloques que
solo muestren contenido y no reciban una respuesta.

## Principios de diseño

- Central y los nodos deben interpretar las mismas preguntas, imágenes, reglas y claves.
- La validación del builder debe anticipar la validación de publicación de Central.
- La vista previa debe corresponder con la experiencia del alumno.
- Los paquetes publicados son inmutables y mantienen compatibilidad con nodos que aún no se hayan
  actualizado.

## Flujo de autoría

1. Crear un examen y seleccionar uno o más cursos.
2. Crear preguntas de opción múltiple o verdadero/falso.
3. Para opción múltiple, definir si admite varias selecciones y, en ese caso, su regla de puntaje.
4. Opcionalmente subir una imagen para una pregunta y completar su clave de respuesta.
5. Revisar la vista previa y los errores de validación.
6. Guardar un borrador y publicar una versión inmutable.
7. Los nodos reciben el paquete publicado en su próxima sincronización.

## Criterios de validación

- El examen tiene código, título y al menos un curso válido.
- Hay al menos una pregunta.
- Cada pregunta tiene un enunciado válido.
- La opción múltiple tiene al menos dos opciones con valores únicos.
- La clave de respuesta coincide con las opciones; una selección única tiene una sola respuesta
  correcta.
- `scoringPolicy` solo aparece en opción múltiple con `multiple: true` y usa un valor conocido.
- Cada `imageAssetId` existe en la misma versión.
- Los assets usan JPEG, PNG o WebP y no superan 2 MiB.

## Trabajo futuro

- Mejorar la importación masiva de preguntas y opciones desde archivos tabulares.
- Agregar bancos de preguntas reutilizables y plantillas por materia o curso.
- Ampliar la vista previa y las herramientas de impresión.
- Evaluar otros tipos de respuesta solo mediante una extensión coordinada del contrato Central,
  del paquete de sincronización y de las aplicaciones Local.

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Las preguntas divergen entre Central y Local | Mantener pruebas de contrato y ejemplos de paquete compartidos |
| Los paquetes incluyen datos innecesarios | Publicar solo imágenes referenciadas y validar el checksum del paquete |
| Una regla de puntaje no está disponible en un nodo antiguo | Mantener el campo de compatibilidad de paquete hasta actualizar todos los nodos |
| Los cambios afectan versiones ya publicadas | Crear una nueva versión; no modificar paquetes publicados |
