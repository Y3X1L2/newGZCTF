import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameAdminOutletContext } from '../GameAdminShell'
import { AdminGameTeamLabPage } from './AdminGameTeamLabPage'
import { useGameTeamLab } from './useGameTeamLab'

vi.mock('./useGameTeamLab', () => ({ useGameTeamLab: vi.fn() }))

const game = { id: 8, title: '企业攻防赛', gameType: 'Penetration' } as GameAdminOutletContext['game']
const binding = { gameId: 8, topologyId: 'topology-1', activeReleaseId: 'release-1', maxResetCount: 2, objectiveRevision: 0, objectives: [] }
const rollout = {
  id: 'rollout-1', releaseId: 'release-1', status: 'ready' as const,
  preparationRequested: true, desiredAccessOpen: false, drainRequested: false,
  counts: { total: 1, pending: 0, provisioning: 0, ready: 1, accessOpen: 0, failed: 0, draining: 0, destroyed: 0, paused: 0 },
  preparedAt: 1, accessOpenedAt: null, drainingAt: null, completedAt: null, createdAt: 1, updatedAt: 2, error: null,
}
const target = {
  id: 'target-1', externalSubject: 'team:12', teamId: 12, displayName: 'Alpha', runtimeId: 'runtime-1',
  status: 'ready' as const, operationId: null, runtimeStatus: 'running' as const, runtimeStage: 'running',
  createdAt: 1, updatedAt: 2, error: null,
}

describe('AdminGameTeamLabPage', () => {
  beforeEach(() => {
    vi.mocked(useGameTeamLab).mockReturnValue({
      state: { binding, rollout }, stateError: undefined,
      releases: [{ topologyId: 'topology-1', topologyName: '企业网络', releaseId: 'release-1', version: 3, networkCount: 4, assetCount: 9, publishedAt: 1 }],
      releasesError: undefined,
      topology: { id: 'topology-1', revision: 1, schemaVersion: 2, definition: { name: '企业网络', networks: [], infrastructure: [], assets: [], connections: [], dependencies: [], observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 'optional' } }, editor: { networks: {}, assets: {}, infrastructure: {} }, createdAt: 1, updatedAt: 1 },
      topologyError: undefined, topologyLoading: false,
      isLoading: false, isRefreshing: false, mutateState: vi.fn(),
      targets: { page: { items: [target], nextCursor: null }, error: undefined, isLoading: false, isRefreshing: false, cursor: { cursor: null, page: 1, canGoBack: false, next: vi.fn(), previous: vi.fn(), reset: vi.fn() }, mutate: vi.fn() },
    })
  })

  it('shows release, rollout counts and a runtime detail link from the target drawer', () => {
    render(
      <MemoryRouter initialEntries={['/admin/games/8/teamlab']}>
        <Routes>
          <Route element={<Outlet context={{ game, mutateGame: vi.fn() }} />} path="/admin/games/:gameId">
            <Route element={<AdminGameTeamLabPage />} path="teamlab" />
          </Route>
        </Routes>
      </MemoryRouter>
    )

    expect(screen.getByRole('heading', { name: 'TeamLab 编排' })).toBeInTheDocument()
    expect(screen.getByText('企业网络 v3')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '得分目标' })).toBeInTheDocument()
    expect(screen.getByText('Alpha')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('row', { name: /Alpha/ }))
    expect(screen.getByRole('link', { name: '打开运行时详情' })).toHaveAttribute('href', '/admin/teamlab/topology-1/runtimes/runtime-1')
  })
})
