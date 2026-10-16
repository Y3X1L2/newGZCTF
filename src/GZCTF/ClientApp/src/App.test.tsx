import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'

vi.mock('@Hooks/useConfig', () => ({ useBanner: vi.fn() }))
vi.mock('./vnext/app/VNextApp', async () => {
  const { AdminLogsPage } = await import('./vnext/features/admin/logs/AdminLogsPage')
  return { VNextApp: AdminLogsPage }
})
vi.mock('./vnext/features/admin/logs/useAdminLogs', () => ({
  useAdminLogs: () => ({
    logs: {
      contract: 'cursor',
      items: [
        {
          id: 1,
          time: 1,
          actorKind: 'System',
          eventCode: 'node.capability.changed',
          workerNodeName: '运行节点',
          outcome: 'Observed',
        },
      ],
      nextCursor: null,
    },
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
  }),
}))
vi.mock('./vnext/features/admin/logs/useAdminLogStream', () => ({
  useAdminLogStream: () => ({
    buffered: [],
    dropped: 0,
    connectionState: 'connected',
    retry: vi.fn(),
    consume: vi.fn(),
  }),
}))

describe('production App language runtime', () => {
  it('translates the real log page and drawer without a test language provider', async () => {
    render(
      <MemoryRouter>
        <App />
      </MemoryRouter>
    )
    expect(await screen.findByRole('heading', { name: '系统日志' })).toBeVisible()
    expect(screen.getByRole('button', { name: '刷新历史' })).toBeVisible()
    expect(screen.getByText('节点能力变化')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: /^查看日志详情/ }))
    expect(within(screen.getByRole('dialog')).getByRole('heading', { name: '节点能力变化' })).toBeVisible()
    expect(document.body.textContent).not.toContain('logs.')
  })
})
