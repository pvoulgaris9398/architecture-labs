import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5000',
      '/ws': {
        target: 'ws://127.0.0.1:5000',
        ws: true,
      },
      '/sse': {
        target: 'http://127.0.0.1:5001',
        rewrite: (path) => path.replace(/^\/sse/, ''),
      },
      '/long-polling': {
        target: 'http://127.0.0.1:5002',
        rewrite: (path) => path.replace(/^\/long-polling/, ''),
      },
    },
  },
});
