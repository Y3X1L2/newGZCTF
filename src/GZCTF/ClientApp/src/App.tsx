import { FC, useMemo } from 'react'
import { ErrorBoundary } from 'react-error-boundary'
import { I18nextProvider } from 'react-i18next'
import { SWRConfig } from 'swr'
import { useBanner } from '@Hooks/useConfig'
import '@xterm/xterm/css/xterm.css'
import '@xyflow/react/dist/style.css'
import { createSWRConfig } from './api-client/swr'
import { VNextApp } from './vnext/app/VNextApp'
import { VNextErrorFallback } from './vnext/app/VNextErrorFallback'
import { VNextThemeProvider } from './vnext/app/VNextThemeProvider'
import { vnextI18n } from './vnext/app/i18n'
import './vnext/design/globals.css'
import './vnext/design/tokens.css'

export const App: FC = () => {
  const swrConfig = useMemo(() => createSWRConfig(), [])

  useBanner()

  return (
    <I18nextProvider i18n={vnextI18n}>
      <VNextThemeProvider>
        <ErrorBoundary FallbackComponent={VNextErrorFallback}>
          <SWRConfig value={swrConfig}>
            <VNextApp />
          </SWRConfig>
        </ErrorBoundary>
      </VNextThemeProvider>
    </I18nextProvider>
  )
}
