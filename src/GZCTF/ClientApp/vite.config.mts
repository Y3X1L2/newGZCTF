import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import process from 'process'
import { defineConfig, loadEnv } from 'vite'
import banner from 'vite-plugin-banner'
import { optimizeCssModules } from 'vite-plugin-optimize-css-modules'
import Pages from 'vite-plugin-pages'
import webfontDownload from 'vite-plugin-webfont-dl'
import { i18nVirtualManifest } from './plugins/vite-i18n-virtual-manifest'
import { sanitizeGeneratedOutput } from './plugins/vite-sanitize-generated-output'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd())

  const TARGET = env.VITE_BACKEND_URL ?? 'http://localhost:8080'
  const current = new Date()

  const BANNER =
    `/* YINYU CTF Platform @${env.VITE_APP_GIT_NAME ?? 'unknown'}\n *\n` +
    ` * Commit    : ${env.VITE_APP_GIT_SHA ?? 'Unofficial build version'}\n` +
    ` * Build     : ${env.VITE_APP_BUILD_TIMESTAMP ?? current.toISOString()}\n` +
    ` */`

  console.log(`Using backend URL: ${TARGET}`)

  return {
    server: {
      port: 63000,
      proxy: {
        '/api': TARGET,
        '/swagger': TARGET,
        '/assets': TARGET,
        '/hub': { target: TARGET.replace('http', 'ws'), ws: true },
        '/favicon.webp': TARGET,
      },
    },
    resolve: {
      tsconfigPaths: true,
    },
    preview: { port: 64000 },
    build: {
      outDir: 'build',
      assetsDir: 'static',
      cssMinify: 'esbuild',
      cssCodeSplit: false,
      chunkSizeWarningLimit: 2400,
      reportCompressedSize: true,
      rolldownOptions: {
        output: {
          hashCharacters: 'base36',
          chunkFileNames: 'static/[hash].js',
          assetFileNames: 'static/[hash].[ext]',
          entryFileNames: 'static/[name].[hash].js',
        },
      },
    },
    html: { cspNonce: '%nonce%' },
    plugins: [
      react(),
      banner(BANNER),
      webfontDownload(
        [
          'https://fonts.googleapis.com/css2?family=JetBrains+Mono:ital,wght@0,100..800;1,100..800&family=Lexend:wght@100..900&display=swap',
          'https://fonts.googleapis.com/css2?family=Fira+Code:wght@300;400;500;600;700&family=Share+Tech+Mono&display=swap',
        ],
        {
          injectAsStyleTag: false,
          async: false,
        }
      ),
      Pages({ dirs: [{ dir: './src/pages', baseRoute: '', filePattern: '**/*.tsx' }] }),
      i18nVirtualManifest(),
      sanitizeGeneratedOutput(),
      optimizeCssModules(),
      tailwindcss(),
    ],
  }
})
