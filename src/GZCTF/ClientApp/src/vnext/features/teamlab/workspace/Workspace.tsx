import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import flowStyles from '@xyflow/react/dist/style.css?inline'
import { Boxes, Layers3 } from 'lucide-react'
import { lazy, Suspense, useMemo, useRef, useState } from 'react'
import { Route, Routes } from 'react-router'
import '@mantine/core/styles.css'
import { useVNextTheme } from '../../../app/VNextThemeProvider'
import styles from './Workspace.module.css'
import { createWorkspaceApi, WorkspaceApiContext } from './api/client'
import { EnvironmentsPage } from './features/environments/EnvironmentsPage'
import { Loading } from './foundation/Feedback'
import { WorkspaceRouteContext, NavLink } from './routing'
import { workspaceTheme } from './theme'

const WorkbenchPage = lazy(() =>
  import('./features/workbench/WorkbenchPage').then((module) => ({ default: module.WorkbenchPage }))
)
const TemplatesPage = lazy(() =>
  import('./features/templates/TemplatesPage').then((module) => ({ default: module.TemplatesPage }))
)

export function Workspace({ gameId, environmentId }: { gameId?: number; environmentId?: string }) {
  const { theme } = useVNextTheme()
  const root = useRef<HTMLDivElement>(null)
  const [client] = useState(
    () =>
      new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 10000, refetchOnWindowFocus: false } } })
  )
  const api = useMemo(
    () => createWorkspaceApi(`/api/teamlab/netlab/${gameId ? `game/${gameId}` : 'admin'}/api/v1`),
    [gameId]
  )
  const route = useMemo(
    () => ({
      basePath: gameId ? `/games/${gameId}/pentest` : '/admin/teamlab',
      environmentId,
      environmentOnly: Boolean(gameId),
      element: root,
    }),
    [gameId, environmentId]
  )
  return (
    <div ref={root} className={styles.root} data-netlab-workspace data-mantine-color-scheme={theme}>
      <style>{`@scope ([data-netlab-workspace]) { ${flowStyles} }`}</style>
      <MantineProvider
        theme={{
          ...workspaceTheme,
          components: { ...workspaceTheme.components, Portal: { defaultProps: { target: '[data-netlab-workspace]' } } },
        }}
        forceColorScheme={theme}
        cssVariablesSelector="[data-netlab-workspace]"
        getRootElement={() => root.current ?? undefined}
        deduplicateCssVariables={false}
        withGlobalClasses={false}
      >
        <QueryClientProvider client={client}>
          <WorkspaceApiContext.Provider value={api}>
            <WorkspaceRouteContext.Provider value={route}>
              <WorkspaceContent environmentOnly={Boolean(gameId)} environmentId={environmentId} />
            </WorkspaceRouteContext.Provider>
          </WorkspaceApiContext.Provider>
        </QueryClientProvider>
      </MantineProvider>
    </div>
  )
}

function WorkspaceContent({ environmentOnly, environmentId }: { environmentOnly: boolean; environmentId?: string }) {
  return (
    <>
      {!environmentOnly && (
        <nav className={styles.navigation} aria-label="组网导航">
          <NavLink to="/environments">
            <Layers3 size={16} />
            环境
          </NavLink>
          <NavLink to="/templates">
            <Boxes size={16} />
            模板
          </NavLink>
        </nav>
      )}
      <Suspense fallback={<Loading />}>
        {environmentOnly || environmentId ? (
          <WorkbenchPage />
        ) : (
          <Routes>
            <Route path="environments/:id" element={<WorkbenchPage />} />
            <Route path="templates" element={<TemplatesPage />} />
            <Route path="*" element={<EnvironmentsPage />} />
          </Routes>
        )}
      </Suspense>
    </>
  )
}
