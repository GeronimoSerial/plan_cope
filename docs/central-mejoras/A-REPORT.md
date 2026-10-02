# Lote A — Marco institucional

## Cambios

- El footer ahora es hermano de `.central-workspace` dentro del frame; ocupa todo el ancho útil y ya no queda dentro de la columna principal.
- Header institucional, banda, contenido y footer comparten `--central-gutter` y usan el ancho disponible, sin el límite de 1440 px.
- Se añadió “Dirección de Sistemas de Información” junto a “Dirección de Planeamiento e Investigación Educativa”. En móviles de hasta 480 px ambas direcciones se muestran debajo del logo; entre 481 y 760 px pueden envolver en el espacio disponible.
- Se conservaron ribbon, negro, amarillo, tipografías, tokens y estilos de foco existentes.

## Verificación

- `git diff --check`: pasó.
- Revisión estática: ambos nombres quedan en el DOM para móvil y escritorio; texto institucional usa `--ink-soft` sobre blanco y el header conserva foco visible con el acento amarillo sobre la banda oscura.
- Build, tests y comprobación visual no se pudieron ejecutar en este entorno: no hay `node`/`npm` ni ejecutables en `node_modules/.bin`; la superficie de navegador CUA no está habilitada (`CUA_REPL_ENABLED_SURFACES is required`).
- Limitación pendiente para integración: ejecutar `npm run build` y verificar visualmente a 320 px y desktop, incluyendo foco de teclado y alineación del footer.

## Archivos del lote

- `src/Central/PlanCope.Central.Web/app/(app)/layout.tsx`
- `src/Central/PlanCope.Central.Web/app/_components/layout/app-header.tsx`
- `src/Central/PlanCope.Central.Web/app/globals.css`
- `docs/central-mejoras/A-REPORT.md`
