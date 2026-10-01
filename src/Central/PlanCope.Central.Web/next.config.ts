import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Permite redirigir el directorio de build (util en WSL para evitar escribir sobre el mount de Windows).
  ...(process.env.NEXT_DIST_DIR ? { distDir: process.env.NEXT_DIST_DIR } : {})
};

export default nextConfig;
