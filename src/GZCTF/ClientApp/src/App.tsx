import { FC, useMemo } from 'react'
import { ErrorBoundary } from 'react-error-boundary'
import { SWRConfig } from 'swr'
import { useBanner } from '@Hooks/useConfig'
import { createSWRConfig } from './api-client/swr'
import { VNextApp } from './vnext/app/VNextApp'
import { VNextErrorFallback } from './vnext/app/VNextErrorFallback'
import { VNextThemeProvider } from './vnext/app/VNextThemeProvider'
import './vnext/design/globals.css'
import './vnext/design/tokens.css'

export const App: FC = () => {
  const swrConfig = useMemo(() => createSWRConfig(), [])

  useBanner()

  return (
    <VNextThemeProvider>
      <ErrorBoundary FallbackComponent={VNextErrorFallback}>
        <SWRConfig value={swrConfig}>
          <VNextApp />
        </SWRConfig>
      </ErrorBoundary>
    </VNextThemeProvider>
  )
}
