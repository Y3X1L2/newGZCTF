import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError, createWorkspaceApi, WorkspaceApiContext, type Permission } from '../../api/client'
import { WorkspaceRouteContext } from '../../routing'
import ConsolePage from './ConsolePage'
import { consoleHref, type ConsoleSession } from './consoles'

vi.mock('./ConsoleWorkspace', () => ({
  default: ({ session }: { session: ConsoleSession }) => <div data-testid="connection">{session.kind}</div>,
}))
vi.mock('./ConnectionDialog', () => ({
  ConnectionDialog: ({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) => (
    <div role="dialog">
      <button onClick={onClose}>取消</button>
      <button onClick={onSaved}>保存</button>
    </div>
  ),
}))

function openConsole(kind: string, permissions: Permission[] = ['session']) {
  const api = createWorkspaceApi('/api/teamlab/netlab/game/12/api/v1')
  api.environment = vi
    .fn()
    .mockResolvedValue({ spec: { assets: [{ id: 'asset', name: '主机' }], networks: [] }, permissions })
  api.sshSettings = vi.fn().mockRejectedValue(new ApiError(404, '尚未配置'))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MantineProvider>
      <QueryClientProvider client={client}>
        <WorkspaceApiContext.Provider value={api}>
          <WorkspaceRouteContext.Provider
            value={{
              basePath: '/games/12/pentest',
              environmentOnly: true,
              environmentId: 'bound-environment',
              element: { current: null },
            }}
          >
            <MemoryRouter initialEntries={[`/console/asset/${kind}`]}>
              <Routes>
                <Route path="console/:assetId/:kind" element={<ConsolePage />} />
              </Routes>
            </MemoryRouter>
          </WorkspaceRouteContext.Provider>
        </WorkspaceApiContext.Provider>
      </QueryClientProvider>
    </MantineProvider>
  )
  return api
}

describe('standalone asset console', () => {
  it('uses the bound environment and refuses a connection without session permission', async () => {
    const api = openConsole('vnc', ['read'])
    expect(await screen.findByText('无法访问该资产的控制台')).toBeInTheDocument()
    expect(api.environment).toHaveBeenCalledWith('bound-environment')
    expect(screen.queryByTestId('connection')).not.toBeInTheDocument()
  })
  it('rejects an unknown protocol without mounting a connection', () => {
    openConsole('unknown')
    expect(screen.getByText('无效的控制台地址')).toBeInTheDocument()
    expect(screen.queryByTestId('connection')).not.toBeInTheDocument()
  })
  it('can cancel missing SSH settings and reopen them without starting a session', async () => {
    openConsole('ssh')
    await screen.findByRole('dialog')
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByTestId('connection')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '连接设置' }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
  it('connects after SSH settings are saved and reloaded', async () => {
    const api = openConsole('ssh')
    await screen.findByRole('dialog')
    vi.mocked(api.sshSettings).mockResolvedValue({
      username: 'test',
      port: 22,
      authKind: 'password',
      hostKey: 'test-key',
    })
    fireEvent.click(screen.getByRole('button', { name: '保存' }))
    expect(await screen.findByTestId('connection')).toHaveTextContent('ssh')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
  it('keeps player links in the game context and encodes route identifiers', () => {
    expect(
      consoleHref({ basePath: '/games/12/pentest', environmentOnly: true }, 'private-environment', 'a/b', 'serial')
    ).toBe('/games/12/pentest/console/a%2Fb/serial')
    expect(consoleHref({ basePath: '/admin/teamlab', environmentOnly: false }, 'env/one', 'asset', 'vnc')).toBe(
      '/admin/teamlab/environments/env%2Fone/console/asset/vnc'
    )
  })
})
