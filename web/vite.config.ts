import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Dev: Vite chạy ở :5173 và proxy API sang ASP.NET Core ở :5000.
// Build: xuất ra ../src/NetTranslate.Api/wwwroot để API phục vụ luôn Web UI.
const api = 'http://localhost:5000'
const proxied = ['/translate', '/translate_file', '/detect', '/languages', '/frontend', '/download_file', '/admin', '/health', '/scalar', '/openapi']

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: Object.fromEntries(proxied.map((p) => [p, { target: api, changeOrigin: false }])),
  },
  build: {
    outDir: '../src/NetTranslate.Api/wwwroot',
    emptyOutDir: true,
  },
})
