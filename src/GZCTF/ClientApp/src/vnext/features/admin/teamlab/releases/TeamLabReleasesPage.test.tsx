import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabAdminApi, teamLabRuntimeApi, type TeamLabRelease } from '../api'
import { TeamLabReleasesPage } from './TeamLabReleasesPage'
import { ReleaseReadinessPanel } from './ReleaseReadinessPanel'

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

  it('scrolls to runtime history after the legacy list route loads asynchronously', async () => {
    const scroll = vi.fn()
    Object.defineProperty(Element.prototype, 'scrollIntoView', { configurable: true, value: scroll })
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => { callback(0); return 1 })
    render(<MemoryRouter initialEntries={[`/admin/teamlab/${release.topologyId}/releases#runtimes`]}>
      <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><TeamLabReleasesPage /></SWRConfig>
    </MemoryRouter>)
    await waitFor(() => expect(scroll).toHaveBeenCalled())
    delete (Element.prototype as { scrollIntoView?: () => void }).scrollIntoView
  })

  it('shows cached nodes separately from placement blockers', () => {
    render(<MemoryRouter><ReleaseReadinessPanel
      readiness={{
        topologyId: release.topologyId, releaseId: release.id, ready: false, plan: null,
        images: [{ imageTemplateId: 1, name: 'Windows 模板', imageType: 'qcow2', eligibleNodeCount: 2, readyNodeCount: 1, pendingNodeCount: 1, failedNodeCount: 0 }],
        latestTrialRuntime: null, blockingReasons: ['网络组 entry/internal 需在同一节点放置。'],
      }}
      creatingTrial={false} preparingImages={false} onCreateTrial={vi.fn()} onPrepareImages={vi.fn()}
    /></MemoryRouter>)

    expect(screen.getByText('暂不可启动')).toBeInTheDocument()
    expect(screen.queryByText('需要准备镜像')).not.toBeInTheDocument()
    expect(screen.getByText('网络组 entry/internal 需在同一节点放置。')).toBeInTheDocument()
    fireEvent.click(screen.getByText('镜像分发详情'))
    expect(screen.getByText('已缓存 / 合格节点')).toBeInTheDocument()
    expect(screen.getByText('1/2')).toBeInTheDocument()
    expect(screen.getByText(/其他节点未缓存或分发失败不单独阻止启动/)).toBeInTheDocument()
  })

  it('allows cold-cache startup and explains that only selected nodes download', () => {
    render(<MemoryRouter><ReleaseReadinessPanel
      readiness={{
        topologyId: release.topologyId, releaseId: release.id, ready: true, plan: null,
        images: [{ imageTemplateId: 1, name: 'Windows 模板', imageType: 'qcow2', eligibleNodeCount: 2, readyNodeCount: 0, pendingNodeCount: 0, failedNodeCount: 0 }],
        latestTrialRuntime: null, blockingReasons: [],
      }}
      creatingTrial={false} preparingImages={false} onCreateTrial={vi.fn()} onPrepareImages={vi.fn()}
    /></MemoryRouter>)

    expect(screen.getByRole('button', { name: '启动环境' })).toBeEnabled()
    expect(screen.getByText(/缺少的缓存将在选定节点下载/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '预热合格节点' })).toBeEnabled()
    expect(screen.getByText(/手动预热会向所有合格节点分发/)).toBeInTheDocument()
  })

  it('keeps start available when only one of two eligible nodes has a cached image', () => {
    render(<MemoryRouter><ReleaseReadinessPanel
      readiness={{
        topologyId: release.topologyId, releaseId: release.id, ready: true, plan: null,
        images: [{ imageTemplateId: 1, name: 'Windows 模板', imageType: 'qcow2', eligibleNodeCount: 2, readyNodeCount: 1, pendingNodeCount: 1, failedNodeCount: 0 }],
        latestTrialRuntime: null, blockingReasons: [],
      }}
      creatingTrial={false} preparingImages={false} onCreateTrial={vi.fn()} onPrepareImages={vi.fn()}
    /></MemoryRouter>)

    expect(screen.getByText('可以启动运行环境')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '启动环境' })).toBeEnabled()
  })
})
