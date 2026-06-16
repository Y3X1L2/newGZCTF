import { App } from '@App'
import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import resourcesToBackend from 'i18next-resources-to-backend'
import ReactDOM from 'react-dom/client'
import { initReactI18next } from 'react-i18next'
import { BrowserRouter } from 'react-router'
import manifest from 'virtual:i18n-manifest'
import { convertLanguage, LanguageProvider } from '@Utils/I18n'

const staleChunkReloadKey = 'gzctf:stale-chunk-reload'

function errorMessageOf(reason: unknown) {
  if (reason instanceof Error) return reason.message
  if (typeof reason === 'string') return reason
  if (typeof reason === 'object' && reason && 'message' in reason) return String(reason.message)
  return ''
}

function isStaleChunkError(reason: unknown) {
  const message = errorMessageOf(reason)
  return /dynamically imported module|Failed to load module script|Importing a module script failed|Unable to preload CSS/i.test(
    message
  )
}

function reloadForFreshAssets() {
  const now = Date.now()
  const lastReload = Number(window.sessionStorage.getItem(staleChunkReloadKey) ?? '0')

  if (Number.isFinite(lastReload) && now - lastReload < 30000) return

  window.sessionStorage.setItem(staleChunkReloadKey, String(now))
  window.location.reload()
}

window.addEventListener('vite:preloadError', (event) => {
  event.preventDefault()
  reloadForFreshAssets()
})

window.addEventListener('unhandledrejection', (event) => {
  if (!isStaleChunkError(event.reason)) return
  event.preventDefault()
  reloadForFreshAssets()
})

window.addEventListener(
  'error',
  (event) => {
    if (!isStaleChunkError(event.error ?? event.message)) return
    event.preventDefault()
    reloadForFreshAssets()
  },
  true
)

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .use(
    // implement by custom vite plugin, see plugins/vite-i18n-virtual-manifest.ts
    resourcesToBackend(async (lang: string, _: string) => {
      const file = manifest[lang.toLowerCase()]
      if (!file) return {}
      const response = await fetch(`/static/${file}`)
      return response.json()
    })
  )
  .init({
    fallbackLng: convertLanguage,
    interpolation: {
      escapeValue: false,
    },
    detection: {
      convertDetectedLanguage: convertLanguage,
    },
  })

const app = ReactDOM.createRoot(document.getElementById('root')!)

app.render(
  <BrowserRouter>
    <LanguageProvider>
      <App />
    </LanguageProvider>
  </BrowserRouter>
)
