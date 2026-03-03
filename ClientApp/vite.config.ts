import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
  ],
  server: {
    port: 44417,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5175',
        changeOrigin: true,
      }
    }
  }
})
