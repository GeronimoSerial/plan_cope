import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  base: "/",
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true
  },
  build: {
    // Chromium/WebView2-only evergreen runtime: WebView2 Evergreen auto-updates
    // with the installed Chromium, so shipping a modern Chrome floor is safe and
    // avoids Vite's conservative "baseline-widely-available" down-transpilation.
    // chrome120 (Dec 2023) already covers the full ES2022 feature set; anything
    // older than that no longer exists in a current Evergreen WebView2 install.
    target: "chrome120",
    // Split vendor (react, react-dom, scheduler) into its own chunk so the
    // app bundle stays small and cacheable. Vite 8 (Rolldown) dropped the
    // Rollup-style `build.manualChunks`; the supported equivalent is
    // `rollupOptions.output.codeSplitting.groups`.
    rollupOptions: {
      output: {
        codeSplitting: {
          groups: [
            {
              name: "vendor",
              test: /node_modules\/(react|react-dom|scheduler)\//
            }
          ]
        }
      }
    }
  }
});
