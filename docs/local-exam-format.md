# Formato local de examenes

Los examenes preparados manualmente se importan como JSON en la API local:

```http
POST /api/exams/import
Content-Type: application/json
```

Usa `docs/local-exam-format.json` como plantilla minima. Para probar visualizacion y flujo completo, usa `docs/local-exam-extensive-sample.json`, que incluye textos, 3 imagenes, opciones multiples, verdadero/falso, respuestas cortas y preguntas obligatorias/opcionales. Al importar, la API valida el contenido, guarda los bloques en SQLite y copia los assets de imagen a la carpeta local configurada con `Local:AssetsPath`. Si no se configura, usa `local-assets` junto al ejecutable de la API.

## Campos principales

- `id`: opcional, estable para reimportar sin duplicar. Si falta, se genera uno deterministico.
- `examCode`, `title`: obligatorios.
- `grade`, `division`, `subject`: metadatos usados por el host local para filtrar y mostrar el examen.
- `versionNumber`: opcional, por defecto `1`.
- `assets`: imagenes locales. Cada asset requiere `id`, `fileName`, `mimeType` con prefijo `image/` y `contentBase64`.
- `blocks`: bloques ordenados del examen.

## Tipos de bloque

- `text`: requiere `config.content`.
- `image`: requiere `config.assetId`, que debe apuntar a un asset importado. Acepta `alt` y `caption`.
- `multiple_choice`: requiere `config.question` y al menos dos `config.options` con `value` y `label`; los `value` deben ser unicos.
- `true_false`: requiere `config.question`.
- `short_answer`: requiere `config.prompt`.

`validation.required: true` marca una pregunta como obligatoria en la pantalla `/examen`. Los bloques `text` e `image` son informativos y no generan respuesta.

Reimportar el mismo `id` actualiza metadata, bloques, assets y claves de respuesta, y elimina del examen local los bloques/assets/keys que ya no esten en el JSON.
