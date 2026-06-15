import { Center, MantineProvider } from '@mantine/core'
import { DatesProvider } from '@mantine/dates'
import { emotionTransform, MantineEmotionProvider } from '@mantine/emotion'
import { ModalsProvider } from '@mantine/modals'
import { Notifications } from '@mantine/notifications'
import { FC, Suspense } from 'react'
import { ErrorBoundary } from 'react-error-boundary'
import { useTranslation } from 'react-i18next'
import { useLocation, useRoutes } from 'react-router'
import { SWRConfig } from 'swr'
import routes from '~react-pages'
import { ErrorFallback } from '@Components/ErrorFallback'
import { WsrxProvider } from '@Components/WsrxProvider'
import { SignalField } from '@Components/yinyu/SignalField'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuPointerGlow } from '@Components/yinyu/YinyuPointerGlow'
import { useYinyuVisualTheme, YinyuThemeToggle } from '@Components/yinyu/YinyuThemeToggle'
import { YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import { localCacheProvider } from '@Utils/Cache'
import { useLanguage } from '@Utils/I18n'
import { useCustomTheme } from '@Utils/ThemeOverride'
import { useBanner } from '@Hooks/useConfig'
import { fetcher } from '@Api'
import '@mantine/core/styles.css'
import '@mantine/dates/styles.css'
import '@mantine/dropzone/styles.css'
import '@mantine/notifications/styles.css'
import './styles/App.css'
import './styles/YinyuDesignLab.css'
import './styles/YinyuTheme.css'
import './styles/YinyuRefinement.css'

const RouteLoading = () => (
  <Center h='100vh' w='100vw' className='route-loader-screen'>
    <YinyuRouteLoader title='YINYU' description={'\u6b63\u5728\u8f7d\u5165\u6f14\u7ec3\u4fe1\u53f7\u4e0e\u9875\u9762\u72b6\u6001'} />
  </Center>
)

export const App: FC = () => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const { theme } = useCustomTheme()
  const [visualTheme, setVisualTheme] = useYinyuVisualTheme()
  const location = useLocation()
  const routeElement = useRoutes(routes)
  useBanner()

  const path = location.pathname
  const isAdminRoute = path.startsWith('/admin')
  const isGameEntryRoute = /^\/games\/\d+\/?$/.test(path)
  const isGameWorkspaceRoute = /^\/games\/\d+\/(challenges|scoreboard|theory|theory-scoreboard|awdp|pentest|monitor)(\/|$)/.test(path)
  const isCrystalTheme = visualTheme === 'crystal'
  const useReactBitsBackdrop = isAdminRoute || isGameWorkspaceRoute
  const suppressSignalField = useReactBitsBackdrop || isGameEntryRoute

  return (
    <MantineProvider defaultColorScheme='dark' forceColorScheme='dark' theme={theme} stylesTransform={emotionTransform}>
      <MantineEmotionProvider>
        <ErrorBoundary FallbackComponent={ErrorFallback}>
          {useReactBitsBackdrop ? <YinyuGameBendsBackground className='yy-root-reactbits-bg' /> : null}
          {!suppressSignalField ? <SignalField /> : null}
          {isCrystalTheme ? <YinyuPointerGlow /> : null}
          <YinyuThemeToggle theme={visualTheme} onChange={setVisualTheme} />
          <Notifications zIndex={5000} />
          <DatesProvider settings={{ locale }}>
            <ModalsProvider labels={{ confirm: t('common.modal.confirm'), cancel: t('common.modal.cancel') }}>
              <SWRConfig
                value={{
                  refreshInterval: 10000,
                  keepPreviousData: true,
                  provider: localCacheProvider,
                  fetcher,
                }}
              >
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
