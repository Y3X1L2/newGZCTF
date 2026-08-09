import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabAdminApi, type TeamLabTopologyDetail } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabRuntimesPage } from './TeamLabRuntimesPage'

vi.mock('../shared/TeamLabSceneShell', () => ({ useTeamLabScene: vi.fn() }))

const scene: TeamLabTopologyDetail = {
  id: '019f0000-0000-7000-8000-000000000001',
  revision: 3,
  schemaVersion: 2,
  definition: {
    name: '企业混合网络',
    networks: [],
    infrastructure: [],
    assets: [],
    connections: [],
    dependencies: [],
    observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 'optional' },
  },
  editor: { networks: {}, assets: {}, infrastructure: {} },
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
}

const runtime = {
  id: '019f0000-0000-7000-8000-000000000010',
  releaseId: '019f0000-0000-7000-8000-000000000020',
  status: 'running' as const,
  stage: 'runtime-ready',
  openForAccess: true,
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
  error: null,
}

function renderPage() {
  return render(
    <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
      <MemoryRouter initialEntries={[`/admin/teamlab/${scene.id}/runtimes`]}>
        <Routes>
          <Route path="/admin/teamlab/:topologyId/runtimes" element={<TeamLabRuntimesPage />} />
          <Route path="/admin/teamlab/:topologyId/runtimes/:runtimeId" element={<div>运行详情已打开</div>} />
        </Routes>
      </MemoryRouter>
    </SWRConfig>
  )
}

describe('TeamLabRuntimesPage', () => {
  beforeEach(() => {
    vi.mocked(useTeamLabScene).mockReturnValue({ scene })
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined)
  })

  it('renders server-paged runtimes and opens the selected detail', async () => {
    vi.spyOn(teamLabAdminApi, 'listTrialRuntimes').mockResolvedValue({ items: [runtime], nextCursor: null })
    renderPage()

    const row = await screen.findByRole('row', { name: /runtime-ready/ })
    expect(row).toHaveTextContent('已开放')
    fireEvent.click(row)
    expect(await screen.findByText('运行详情已打开')).toBeInTheDocument()
  })

  it('requests the next page with the server cursor', async () => {
    const list = vi.spyOn(teamLabAdminApi, 'listTrialRuntimes')
      .mockResolvedValueOnce({ items: [runtime], nextCursor: 'cursor-next' })
      .mockResolvedValueOnce({ items: [{ ...runtime, id: '019f0000-0000-7000-8000-000000000011' }], nextCursor: null })
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: '下一页' }))
    await waitFor(() => expect(list).toHaveBeenCalledWith(scene.id, 'cursor-next', 30))
  })
})
