import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { createWorkspaceApi, WorkspaceApiContext, type Environment } from '../../api/client'
import { WorkspaceRouteContext } from '../../routing'
import TrafficWorkspace from './TrafficWorkspace'

vi.mock('./TrafficTimeline', () => ({ TrafficTimeline: () => <div>趋势图</div> }))
vi.mock('./useTopology', () => ({ useTopology: () => ({ data: { nodes: [] } }) }))
vi.mock('./TrafficGraph', async (original) => {
  const actual = await original<typeof import('./TrafficGraph')>()
  return {
    ...actual,
    TrafficGraph: ({ onSelect }: { onSelect: (value?: { asset: string }) => void }) => (
      <div>
        <button onClick={() => onSelect({ asset: 'dc' })}>选择 DC</button>
        <button onClick={() => onSelect({ asset: 'web' })}>选择 Web</button>
      </div>
    ),
  }
})
vi.mock('./PacketDetailDialog', () => ({
  PacketDetailDialog: ({ opened }: { opened: boolean }) => (opened ? <div role="dialog">报文详情</div> : null),
}))

describe('inline traffic inspection', () => {
  beforeAll(() => {
    HTMLElement.prototype.scrollTo = vi.fn()
  })
  it('fetches the selected machine packets directly, switches scope, reuses details and collapses on clear', async () => {
    const api = createWorkspaceApi('/test')
    api.security = vi.fn().mockResolvedValue({ alerts: [], sensors: [], counts: { high: 0, medium: 0, low: 0 } })
    api.traffic = vi.fn().mockResolvedValue({ flows: [], errors: {} })
    api.environmentMetrics = vi.fn().mockResolvedValue({ series: [] })
    api.environmentPackets = vi.fn().mockImplementation((_id, filters) =>
      Promise.resolve({
        packets: [
          {
            id: filters.assetId,
            number: 1,
            time: '2026-11-11T00:00:00Z',
            source: filters.assetId,
            sourcePort: 1234,
            destination: '192.0.2.1',
            destinationPort: 443,
            protocol: 'TLS',
            length: 60,
            summary: 'Application Data',
          },
        ],
        errors: {},
      })
    )
    api.environmentPacket = vi.fn().mockResolvedValue({})
    const environment = {
      id: 'environment',
      view: { roles: {} },
      spec: {
        networks: [],
        assets: [
          { id: 'dc', name: 'DC', interfaces: [] },
          { id: 'web', name: 'Web', interfaces: [] },
        ],
      },
    } as unknown as Environment
    render(
      <MantineProvider>
        <MemoryRouter initialEntries={['/?view=traffic']}>
          <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <WorkspaceApiContext.Provider value={api}>
              <WorkspaceRouteContext.Provider
                value={{
                  basePath: '/test',
                  environmentOnly: false,
                  environmentId: 'environment',
                  element: { current: document.createElement('div') },
                }}
              >
                <TrafficWorkspace environment={environment} />
              </WorkspaceRouteContext.Provider>
            </WorkspaceApiContext.Provider>
          </QueryClientProvider>
        </MemoryRouter>
      </MantineProvider>
    )
    fireEvent.click(await screen.findByRole('button', { name: '选择 DC' }))
    const panel = screen.getByRole('region', { name: '所选对象报文' })
    await waitFor(() => expect(within(panel).getByText('dc:1234')).toBeInTheDocument())
    expect(api.environmentPackets).toHaveBeenLastCalledWith(
      'environment',
      expect.objectContaining({ assetId: 'dc' }),
      expect.any(AbortSignal)
    )
    expect(screen.queryByRole('button', { name: '返回流量概览' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '选择 Web' }))
    await waitFor(() => expect(within(panel).getByText('web:1234')).toBeInTheDocument())
    expect(within(panel).queryByText('dc:1234')).not.toBeInTheDocument()
    fireEvent.click(within(panel).getByRole('button', { name: '查看第 1 帧' }))
    expect(screen.getByRole('dialog')).toHaveTextContent('报文详情')
    fireEvent.click(within(panel).getByRole('button', { name: '关闭报文' }))
    expect(screen.queryByRole('region', { name: '所选对象报文' })).not.toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
