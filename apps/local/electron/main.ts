/**
 * apps/local — Electron main process stub.
 *
 * Fase 1 only creates a minimal BrowserWindow that points to the Fastify
 * health endpoint. Full UI and IPC are deferred to Fase 2.
 *
 * Run with `pnpm --filter @plan-cope/local exec electron electron/main.ts`
 * (after Fase 2 wires up a renderer). For now this file is a placeholder
 * kept under `if (false)` so the import graph is valid; the real entry
 * point activates when `electron` is installed as a runtime dep.
 */
import { app, BrowserWindow } from 'electron';

void app;
void BrowserWindow;

// Real implementation in Fase 2:
// const createWindow = (): void => {
//   const win = new BrowserWindow({ width: 1280, height: 800 });
//   void win.loadURL(`http://localhost:${process.env.PORT ?? 3001}/`);
// };
// app.whenReady().then(createWindow);
