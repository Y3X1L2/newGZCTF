import { App } from '@App'
import ReactDOM from 'react-dom/client'
import { createBrowserRouter, RouterProvider } from 'react-router'

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

document.documentElement.lang = 'zh-CN'

const app = ReactDOM.createRoot(document.getElementById('root')!)
const router = createBrowserRouter([{ path: '*', element: <App /> }])

app.render(<RouterProvider router={router} />)
