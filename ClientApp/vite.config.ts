import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
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
