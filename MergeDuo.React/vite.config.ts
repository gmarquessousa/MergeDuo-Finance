import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'apple-touch-icon-180x180.png'],
      manifest: {
        name: 'Merge Duo · Finanças',
        short_name: 'Merge Duo',
        description: 'Visão financeira compartilhada do casal',
        theme_color: '#000000',
        background_color: '#000000',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        scope: '/',
        lang: 'pt-BR',
        icons: [
          { src: 'pwa-64x64.png',          sizes: '64x64',   type: 'image/png' },
          { src: 'pwa-192x192.png',         sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png',         sizes: '512x512', type: 'image/png' },
          { src: 'maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // Cache only static assets — não cachear HTML para sempre pegar JS novo no Safari
        globPatterns: ['**/*.{js,css,svg,png,ico,woff,woff2,html}'],
        navigateFallback: '/index.html',
        cleanupOutdatedCaches: true,
        clientsClaim: true,
        skipWaiting: true,
        // Nunca interceptar requisições de auth (Google + nosso backend)
        navigateFallbackDenylist: [/^\/auth/, /^\/users/, /^\/\.well-known/, /^\/api/],
        runtimeCaching: [
          {
            // Auth/API: sempre network, sem cache
            urlPattern: ({ url }) =>
              url.pathname.startsWith('/auth') ||
              url.pathname.startsWith('/users') ||
              url.pathname.startsWith('/.well-known') ||
              url.pathname.startsWith('/api') ||
              url.hostname.endsWith('google.com') ||
              url.hostname.endsWith('googleapis.com'),
            handler: 'NetworkOnly',
          },
        ],
      },
    }),
  ],
})
