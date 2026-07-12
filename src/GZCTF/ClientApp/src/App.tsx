import { Center, MantineProvider } from '@mantine/core'
import { DatesProvider } from '@mantine/dates'
import { emotionTransform, MantineEmotionProvider } from '@mantine/emotion'
import { ModalsProvider } from '@mantine/modals'
import { Notifications } from '@mantine/notifications'
import { FC, lazy, Suspense, useEffect, useMemo } from 'react'
import { ErrorBoundary } from 'react-error-boundary'
import { useTranslation } from 'react-i18next'
import { useLocation, useRoutes } from 'react-router'
import { SWRConfig } from 'swr'
import routes from '~react-pages'
import { ErrorFallback } from '@Components/ErrorFallback'
import { WsrxProvider } from '@Components/WsrxProvider'
import { YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import yinyuIcon from './assets/yinyu-icon-transparent.png'
import { localCacheProvider } from '@Utils/Cache'
import { useLanguage } from '@Utils/I18n'
import { useCustomTheme } from '@Utils/ThemeOverride'
import { useBanner, useConfig } from '@Hooks/useConfig'
import { createSWRConfig } from './api-client/swr'
import '@mantine/core/styles.css'
import '@mantine/dates/styles.css'
import '@mantine/dropzone/styles.css'
import '@mantine/notifications/styles.css'
import './styles/foundation/tokens.css'
import './styles/App.css'
import './styles/YinyuDesignLab.css'
import './styles/YinyuTheme.css'
import './styles/YinyuRefinement.css'
import './styles/foundation/base.css'

const RouteBackdrop = lazy(() => import('@Components/yinyu/RouteBackdrop').then((module) => ({ default: module.RouteBackdrop })))
const SignalField = lazy(() => import('@Components/yinyu/SignalField').then((module) => ({ default: module.SignalField })))

const RouteLoading = () => (
  <Center h='100vh' w='100vw' className='route-loader-screen'>
    <YinyuRouteLoader title='YINYU' description='正在加载页面内容' />
  </Center>
)

const createZoomedFavicon = (iconUrl: string, signal: AbortSignal) =>
  new Promise<string>((resolve, reject) => {
    const image = new Image()

    image.onload = () => {
      if (signal.aborted) {
        reject(signal.reason)
        return
      }

      const size = 96
      const scale = 1.9
      const canvas = document.createElement('canvas')
      const context = canvas.getContext('2d')

      if (!context) {
        reject(new Error('Canvas is not available'))
        return
      }

      canvas.width = size
      canvas.height = size
      context.clearRect(0, 0, size, size)

      const targetSize = size * scale
      const x = (size - targetSize) / 2
      const y = (size - targetSize) / 2

      context.drawImage(image, x, y, targetSize, targetSize)

      try {
        resolve(canvas.toDataURL('image/png'))
      } catch (error) {
        reject(error)
      }
    }

    image.onerror = reject
    image.decoding = 'async'
    image.crossOrigin = 'anonymous'
    image.src = iconUrl
  })

const usePlatformFavicon = () => {
  const { config } = useConfig()
  const iconUrl = config.logoUrl || yinyuIcon

  useEffect(() => {
    const controller = new AbortController()
    const selector = "link[rel~='icon']"
    let link = document.head.querySelector<HTMLLinkElement>(selector)

    if (!link) {
      link = document.createElement('link')
      link.rel = 'icon'
      document.head.appendChild(link)
    }

    link.href = iconUrl
    link.removeAttribute('type')

    createZoomedFavicon(iconUrl, controller.signal)
      .then((href) => {
        if (!controller.signal.aborted) {
          link.href = href
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          link.href = iconUrl
        }
      })

    return () => controller.abort()
  }, [iconUrl])
}

export const App: FC = () => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const { theme } = useCustomTheme()
  const location = useLocation()
  const routeElement = useRoutes(routes)
  const swrConfig = useMemo(() => createSWRConfig(localCacheProvider), [])
  useBanner()
  usePlatformFavicon()

  const path = location.pathname
  const isAdminRoute = path.startsWith('/admin')
  const isTrainingRoute = path.startsWith('/training')
  const isTeamsRoute = path.startsWith('/teams')
  const isGameEntryRoute = /^\/games\/\d+\/?$/.test(path)
  const isGameWorkspaceRoute = /^\/games\/\d+\/(challenges|scoreboard|theory|theory-scoreboard|awdp|pentest|monitor)(\/|$)/.test(path)
  const useReactBitsBackdrop = isAdminRoute || isTrainingRoute || isTeamsRoute || isGameWorkspaceRoute
  const suppressSignalField = useReactBitsBackdrop || isGameEntryRoute

  return (
    <MantineProvider defaultColorScheme='dark' forceColorScheme='dark' theme={theme} stylesTransform={emotionTransform}>
      <MantineEmotionProvider>
        <ErrorBoundary FallbackComponent={ErrorFallback}>
          <Suspense fallback={null}>{useReactBitsBackdrop ? <RouteBackdrop /> : null}</Suspense>
          <Suspense fallback={null}>{!suppressSignalField ? <SignalField /> : null}</Suspense>
          <Notifications zIndex={5000} />
          <DatesProvider settings={{ locale }}>
            <ModalsProvider labels={{ confirm: t('common.modal.confirm'), cancel: t('common.modal.cancel') }}>
              <SWRConfig value={swrConfig}>
                <WsrxProvider>
                  <Suspense fallback={<RouteLoading />}>{routeElement}</Suspense>
                </WsrxProvider>
              </SWRConfig>
            </ModalsProvider>
          </DatesProvider>
        </ErrorBoundary>
      </MantineEmotionProvider>
    </MantineProvider>
  )
}
