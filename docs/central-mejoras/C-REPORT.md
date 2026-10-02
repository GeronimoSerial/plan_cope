# Lote C — Preview general e individual

## Resultado

- Se agregó `QuestionPreview` como renderer común del preview individual y del examen completo. Muestra el enunciado, la ayuda, la imagen actual, la condición requerida y las opciones; los controles siempre permanecen vacíos y desactivados, sin consultar ni exponer `isCorrect` o `correctAnswer`.
- Cada tarjeta incluye el botón accesible “Vista previa de la pregunta N”. El diálogo enfoca su contenido al abrir, cierra con Escape y devuelve el foco al botón que lo abrió. Renderiza el valor actual del builder cada vez que se abre.
- El preview general conserva título, descripción, materia/grados y orden de preguntas. Las preguntas sin texto u opciones muestran un estado legible.
- Se leyó `Local/.../student/components/ExamBlock.tsx` y su CSS como referencia; no se modificó Local. Para coincidir con el renderer publicado actual, la opción múltiple también se representa con radios: `ExamBlock` procesa el bloque `MultipleChoice` con `input type="radio"`.

## Verificación

- Pruebas específicas: `question-preview.test.tsx`, `exam-preview.test.tsx` y `question-card.test.tsx` — 5 pruebas aprobadas.
- ESLint sobre los seis archivos del lote — limpio.
- `tsc --noEmit` llega a un error ajeno al lote: `app/_components/exams/create-exam-dialog.tsx:73` (`TS2554: Expected 0 arguments, but got 1`), archivo que pertenece al lote B y cambió en el worktree compartido.
- La cobertura comprueba contenido/orden, los tres tipos soportados, imagen/ayuda/requerida, pregunta incompleta, controles sin seleccionar y apertura/cierre con recuperación de foco. No se hizo una captura de navegador; la comparación visual se basó en `ExamBlock.tsx` y sus estilos fuente.

## Archivos del lote

- `app/_components/builder/question-card.tsx`
- `app/_components/builder/exam-preview.tsx`
- `app/_components/builder/question-preview.tsx`
- pruebas de esos tres componentes
- `docs/central-mejoras/C-REPORT.md`
