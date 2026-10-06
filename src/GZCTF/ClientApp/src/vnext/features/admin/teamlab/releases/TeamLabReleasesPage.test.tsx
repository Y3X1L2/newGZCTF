import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabAdminApi, teamLabRuntimeApi, type TeamLabRelease } from '../api'
import { TeamLabReleasesPage } from './TeamLabReleasesPage'

const navigate = vi.fn()
vi.mock('react-router', async () => {
  const actual = await vi.importActual<typeof import('react-router')>('react-router')
  return { ...actual, useNavigate: () => navigate }
})
vi.mock('../shared/TeamLabSceneShell', () => ({
  useTeamLabScene: () => ({
    scene: {
      id: '019f0000-0000-7000-8000-000000000001',
      revision: 4,
      schemaVersion: 2,
      definition: { name: '企业域演练', networks: [], assets: [], infrastructure: [], connections: [], observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true } },
      editor: { networks: {}, assets: {}, infrastructure: {} },
      createdAt: 1,
      updatedAt: 2,
    },
  }),
}))

const release: TeamLabRelease = {
  id: '019f0000-0000-7000-8000-000000000010', topologyId: '019f0000-0000-7000-8000-000000000001',
  version: 2, sourceRevision: 4, schemaVersion: 2, contentHash: 'sha256:0123456789abcdef',
  publishedBy: 'Teacher A', publisherName: null, publishedAt: 1_784_918_400_000,
}

describe('TeamLabReleasesPage', () => {
  beforeEach(() => {
    navigate.mockReset()
    vi.spyOn(teamLabAdminApi, 'listReleases').mockResolvedValue([release])
    vi.spyOn(teamLabAdminApi, 'releaseReadiness').mockResolvedValue({
      topologyId: release.topologyId, releaseId: release.id, ready: true, images: [], blockingReasons: [], latestTrialRuntime: null,
      plan: { topologyId: release.topologyId, releaseId: release.id, networks: [], assets: [], shards: [], crossShardConnections: 0, requiredCapabilities: [], warnings: [], planHash: 'sha256:plan', managedInfrastructureCount: 0, observationPointEstimate: 0 },
    })
    vi.spyOn(teamLabAdminApi, 'listTrialRuntimes').mockResolvedValue({ items: [], nextCursor: null })
  })

  it('shows server readiness and creates a trial from the selected immutable release', async () => {
    const create = vi.spyOn(teamLabRuntimeApi, 'createTrial').mockResolvedValue({
      id: '019f0000-0000-7000-8000-000000000020', releaseId: release.id, generation: 1, status: 'pending', stage: 'pending', openForAccess: false, shards: [], networks: [], assets: [], createdAt: 1, updatedAt: null, error: null,
    })
    render(<MemoryRouter><SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><TeamLabReleasesPage /></SWRConfig></MemoryRouter>)

    expect(await screen.findByRole('heading', { name: '版本与启动' })).toBeInTheDocument()
    expect(await screen.findByText('可以启动运行环境')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '启动环境' }))
    const dialog = await screen.findByRole('dialog', { name: '启动运行环境？' })
    fireEvent.click(within(dialog).getByRole('button', { name: '启动环境' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith(expect.any(String), {
      releaseId: release.id, constraints: null, overlays: null, externalReference: null,
    }))
    expect(navigate).toHaveBeenCalledWith('/admin/teamlab/runtimes/019f0000-0000-7000-8000-000000000020')
  })
})
