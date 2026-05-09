import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import fs from 'fs';
import type { ServerOptions as HttpsServerOptions } from 'https';

const certPath = 'C:/temp/certs/localhost.crt';
const keyPath = 'C:/temp/certs/localhost.key';

let httpsConfig: HttpsServerOptions | undefined;
if (fs.existsSync(certPath) && fs.existsSync(keyPath)) {
  httpsConfig = {
    key: fs.readFileSync(keyPath),
    cert: fs.readFileSync(certPath),
  };
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    strictPort: false,
    https: httpsConfig,
    proxy: {
      '/api': {
        target: 'https://localhost:7216',
        secure: false,
        changeOrigin: true,
      },
      '/auth': {
        target: 'https://localhost:7216',
        secure: false,
        changeOrigin: true,
      },
    },
  },
});
