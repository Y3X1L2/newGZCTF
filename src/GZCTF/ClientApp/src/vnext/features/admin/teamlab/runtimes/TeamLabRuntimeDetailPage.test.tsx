import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { TeamLabRuntimeDetailPage } from './TeamLabRuntimeDetailPage'
import { useRuntimeEvents } from './useRuntimeEvents'
import { useTeamLabRuntime } from './useTeamLabRuntime'
import { useTrafficObservability } from './useTrafficObservability'

vi.mock('./useTeamLabRuntime', () => ({ useTeamLabRuntime: vi.fn() }))
vi.mock('./useRuntimeEvents', () => ({
  useRuntimeEvents: vi.fn(),
  emptyTeamLabEventFilters: () => ({ generation: null, stage: '' }),
}))
vi.mock('./useTrafficObservability', () => ({ useTrafficObservability: vi.fn() }))

const runtime: TeamLabRuntime = {
  id: '019f0000-0000-7000-8000-000000000010',
  releaseId: '019f0000-0000-7000-8000-000000000020',
  generation: 2,
  status: 'running',
  stage: 'runtime-ready',
  openForAccess: true,
  shards: [
    {
      id: '019f0000-0000-7000-8000-000000000030',
      workerNodeId: '019f0000-0000-7000-8000-000000000031',
      workerNodeName: 'worker-a',
      status: 'running',
      networkKeys: ['entry'],
      assetKeys: ['web'],
      error: null,
    },
  ],
  networks: [{ key: 'entry', name: '入口网段', cidr: '10.10.0.0/24', gatewayIp: '10.10.0.1' }],
  assets: [
    {
      id: 1,
      key: 'web',
      name: 'Web 服务',
      kind: 'docker',
      runtimeResourceId: 'container-1',
      primaryIp: '10.10.0.10',
      status: 'running',
      error: null,
    },
  ],
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
  error: null,
}

describe('TeamLabRuntimeDetailPage', () => {
  beforeEach(() => {
    vi.mocked(useTeamLabRuntime).mockReturnValue({
      runtime,
      error: undefined,
      isLoading: false,
      isRefreshing: false,
      mutate: vi.fn(),
    })
    vi.mocked(useRuntimeEvents).mockReturnValue({
      events: [],
      error: undefined,
      isLoading: false,
      isRefreshing: false,
      mutate: vi.fn(),
    })
    vi.mocked(useTrafficObservability).mockReturnValue({
      flows: {
        page: { items: [], nextCursor: null, completeness: { complete: true, droppedRecords: 0 } },
        error: undefined,
        isLoading: false,
        isRefreshing: false,
        cursor: { cursor: null, page: 1, canGoBack: false, next: vi.fn(), previous: vi.fn(), reset: vi.fn() },
        mutate: vi.fn(),
      },
      paths: {
        page: { items: [], nextCursor: null, completeness: { complete: true, droppedRecords: 0 } },
        error: undefined,
        isLoading: false,
        isRefreshing: false,
        cursor: { cursor: null, page: 1, canGoBack: false, next: vi.fn(), previous: vi.fn(), reset: vi.fn() },
        mutate: vi.fn(),
      },
    })
  })

  it('renders persisted stage, shard placement and topology without unsupported controls', () => {
    render(
      <MemoryRouter initialEntries={[`/admin/teamlab/topology-a/runtimes/${runtime.id}`]}>
        <Routes>
          <Route path="/admin/teamlab/:topologyId/runtimes/:runtimeId" element={<TeamLabRuntimeDetailPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(screen.getAllByText('runtime-ready')).toHaveLength(2)
    expect(screen.getAllByText('worker-a').length).toBeGreaterThan(0)
    expect(screen.getByText('10.10.0.10')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /暂停|恢复/ })).not.toBeInTheDocument()
  })

  it('loads traffic panels only after the observability tab is selected', () => {
    render(
      <MemoryRouter initialEntries={[`/admin/teamlab/topology-a/runtimes/${runtime.id}`]}>
        <Routes>
          <Route path="/admin/teamlab/:topologyId/runtimes/:runtimeId" element={<TeamLabRuntimeDetailPage />} />
        </Routes>
      </MemoryRouter>
    )
    expect(screen.queryByRole('heading', { name: '流量元数据' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '流量观测' }))
    expect(screen.getByRole('heading', { name: '流量元数据' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '端到端路径' })).toBeInTheDocument()
  })

  it('offers an explicit idempotent cleanup recovery action', () => {
    vi.mocked(useTeamLabRuntime).mockReturnValue({
      runtime: { ...runtime, status: 'cleanup-pending', stage: 'cleanup' },
      error: undefined,
      isLoading: false,
      isRefreshing: false,
      mutate: vi.fn(),
    })
    render(
      <MemoryRouter initialEntries={[`/admin/teamlab/topology-a/runtimes/${runtime.id}`]}>
        <Routes>
          <Route path="/admin/teamlab/:topologyId/runtimes/:runtimeId" element={<TeamLabRuntimeDetailPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(screen.getByRole('button', { name: '继续清理' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: '销毁' })).not.toBeInTheDocument()
  })
})
