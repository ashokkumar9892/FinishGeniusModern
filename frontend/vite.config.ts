import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'

// In development the API runs on http://localhost:5080 and Vite proxies /api to it.
// `npm run build` writes straight into the API's wwwroot so IIS serves one site.
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(__dirname, 'src') } },
  server: {
    // Own port (5173 is commonly taken by other Vite projects); fail instead of silently switching ports.
    port: 5180,
    strictPort: true,
    proxy: { '/api': { target: 'http://localhost:5080', changeOrigin: true } },
  },
  build: {
    outDir: '../backend/FinishGenius.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
})
