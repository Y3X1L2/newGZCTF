import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import flowStyles from '@xyflow/react/dist/style.css?inline'
import { Boxes, Layers3 } from 'lucide-react'
import { lazy, Suspense, useId, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
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
const ConsolePage = lazy(() => import('./features/workbench/ConsolePage'))

export function Workspace({
  gameId,
  environmentId,
  consoleMode = false,
}: {
  gameId?: number
  environmentId?: string
  consoleMode?: boolean
}) {
  const { theme } = useVNextTheme()
  const root = useRef<HTMLDivElement>(null)
  const portalId = useId()
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
    <>
      {/* Keep overlays outside the animated route while retaining workspace-scoped styles. */}
      {createPortal(
        <div className={styles.root} data-netlab-workspace>
          <div id={portalId} data-netlab-theme data-mantine-color-scheme={theme} />
        </div>,
        document.body
      )}
      <div className={`${styles.root} ${consoleMode ? styles.console : ''}`} data-netlab-workspace>
        {/* Mantine's theme ancestor must be inside the CSS scope, not on its boundary. */}
        <div ref={root} className={styles.content} data-netlab-theme data-mantine-color-scheme={theme}>
          <style>{`@scope ([data-netlab-workspace]) { ${flowStyles} }`}</style>
          <MantineProvider
            theme={{
              ...workspaceTheme,
              components: { ...workspaceTheme.components, Portal: { defaultProps: { target: `[id="${portalId}"]` } } },
            }}
            forceColorScheme={theme}
            cssVariablesSelector="[data-netlab-theme]"
            cssVariablesResolver={() => ({
              variables: {
                '--mantine-primary-color-filled': 'var(--yn-color-control-strong)',
                '--mantine-primary-color-filled-hover': 'var(--yn-color-control-strong-hover)',
                '--mantine-color-error': 'var(--yn-color-danger)',
              },
              light: {},
              dark: {},
            })}
            getRootElement={() => root.current ?? undefined}
            deduplicateCssVariables={false}
            withGlobalClasses={false}
          >
            <QueryClientProvider client={client}>
              <WorkspaceApiContext.Provider value={api}>
                <WorkspaceRouteContext.Provider value={route}>
                  {consoleMode ? (
                    <Suspense fallback={<Loading />}>
                      <ConsolePage />
                    </Suspense>
                  ) : (
                    <WorkspaceContent environmentOnly={Boolean(gameId)} environmentId={environmentId} />
                  )}
                </WorkspaceRouteContext.Provider>
              </WorkspaceApiContext.Provider>
            </QueryClientProvider>
          </MantineProvider>
        </div>
      </div>
    </>
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
