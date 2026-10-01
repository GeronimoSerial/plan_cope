# Formato de examen local

Los archivos `docs/local-exam-format.json` y `docs/local-exam-extensive-sample.json` muestran el
modelo de preguntas que Central publica y los nodos locales reciben mediante `GET /api/sync/pull`.
Ambos son ejemplos de referencia; la sincronización transporta un `PublishedExamPackageDto`, no un
endpoint de importación JSON.

## Campos principales

- `id`, `examCode`, `title`: identifican el examen.
- `courses`: claves de curso seleccionadas, por ejemplo `primaria-6` o `secundaria-1`. Central
  publica estas claves como destinos descriptivos `grade`; no se envía un campo `level`.
- `division`, `subject`: metadatos descriptivos de la publicación.
- `versionNumber`: opcional, por defecto `1`.
- `assets`: imágenes referenciadas por preguntas. Cada asset requiere `id`, `fileName`, `mimeType`
  y `contentBase64`. Central acepta JPEG, PNG y WebP de hasta 2 MiB por imagen.
- `blocks`: preguntas ordenadas. Los tipos admitidos son `multiple_choice` y `true_false`.

## Tipos de bloque

- `multiple_choice`: requiere `config.question` y al menos dos `config.options` con `value` y
  `label`. `config.multiple` indica si se puede marcar más de una opción.
- `true_false`: requiere `config.question`.

Ambos tipos aceptan un `config.imageAssetId` opcional que debe identificar un asset de la misma
versión. No hay bloques de contenido sin respuesta: incluí las instrucciones necesarias en el
enunciado de la pregunta.

Para una pregunta `multiple_choice` con `multiple: true`, `config.scoringPolicy` puede ser
`AllOrNothing`, `ProportionalPenalised` o `ProportionalPlain`; si se omite, se usa `AllOrNothing`.
No se envía `scoringPolicy` en preguntas de selección única ni en `true_false`.

`validation.required: true` marca una pregunta como obligatoria. La clave se representa como
`answerKey.correctAnswer` y `answerKey.scoreValue`; para selección múltiple la respuesta correcta
es un array de valores, para selección única es un valor y para verdadero/falso es un booleano.
