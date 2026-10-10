import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabAdminApi, teamLabRuntimeApi, type TeamLabTopologyDetail } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
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
    observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true },
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
          <Route path="/admin/teamlab/runtimes/:runtimeId" element={<div>运行详情已打开</div>} />
        </Routes>
      </MemoryRouter>
    </SWRConfig>
  )
}

describe('TeamLabRuntimesPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.mocked(useTeamLabScene).mockReturnValue({ scene })
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined)
    vi.spyOn(teamLabAdminApi, 'listReleases').mockResolvedValue([])
  })

  it('deletes Destroyed history instead of invoking Destroy again', async () => {
    vi.spyOn(teamLabAdminApi, 'listTrialRuntimes').mockResolvedValue({ items: [{ ...runtime, status: 'destroyed' }], nextCursor: null })
    const remove = vi.spyOn(teamLabRuntimeApi, 'deleteRuntimeRecord').mockResolvedValue(undefined)
    const destroy = vi.spyOn(teamLabRuntimeApi, 'destroyRuntime')
    renderPage()
    const removeButton = await screen.findByRole('button', { name: '删除运行记录' })
    removeButton.focus()
    await userEvent.keyboard('{Enter}')
    const dialog = screen.getByRole('dialog', { name: '删除运行记录' })
    expect(dialog).toHaveTextContent(runtime.id)
    fireEvent.click(within(dialog).getByRole('button', { name: '删除运行记录' }))
    await waitFor(() => expect(remove).toHaveBeenCalledWith(runtime.id))
    expect(destroy).not.toHaveBeenCalled()
  })

  it('renders server-paged runtimes and opens the selected detail', async () => {
    vi.spyOn(teamLabAdminApi, 'listTrialRuntimes').mockResolvedValue({ items: [runtime], nextCursor: null })
    renderPage()

    const row = await screen.findByRole('row', { name: /企业混合网络/ })
    expect(row).toHaveTextContent('已开放')
    const status = within(row).getByText('环境运行中').closest('span')
    expect(status).not.toHaveAttribute('data-pulse')
    expect(status?.querySelector('svg')).not.toBeNull()
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

  it.each([
    ['pending', '等待中'],
    ['planning', '规划中'],
    ['scheduled', '已排队'],
    ['deploying', '部署中'],
    ['probing', '探测中'],
    ['destroying', '销毁中'],
  ] as const)('animates the %s runtime status', (status, label) => {
    render(<TeamLabRuntimeStatusBadge status={status} />)
    expect(screen.getByText(label).closest('span')).toHaveAttribute('data-pulse', 'true')
  })

  it('does not animate cleanup-pending', () => {
    render(<TeamLabRuntimeStatusBadge status="cleanup-pending" />)
    expect(screen.getByText('待清理').closest('span')).not.toHaveAttribute('data-pulse')
  })
})
