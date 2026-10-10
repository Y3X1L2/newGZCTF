import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import { teamLabAdminApi, type TeamLabAdminSceneSummary } from '../api'
import { TeamLabLibraryPage } from './TeamLabLibraryPage'
import { useTeamLabCatalog } from './useTeamLabCatalog'

vi.mock('./useTeamLabCatalog', () => ({ useTeamLabCatalog: vi.fn() }))

const scene: TeamLabAdminSceneSummary = {
  id: '019f0000-0000-7000-8000-000000000001',
  name: '企业域演练',
  ownerId: '019f0000-0000-7000-8000-000000000002',
  ownerDisplayName: 'Teacher A',
  revision: 4,
  schemaVersion: 2,
  networkCount: 3,
  assetCount: 8,
  infrastructureCount: 2,
  latestRelease: {
    id: '019f0000-0000-7000-8000-000000000003',
    version: 2,
    sourceRevision: 4,
    contentHash: '0123456789abcdef',
    publishedAt: 1_784_918_400_000,
  },
  validation: { revision: 4, valid: true, issueCount: 0, validatedAt: 1_784_918_400_000 },
  latestTrialRuntime: null,
  gameReferenceCount: 1,
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
}

function catalog(overrides: Partial<ReturnType<typeof useTeamLabCatalog>> = {}): ReturnType<typeof useTeamLabCatalog> {
  return {
    page: { items: [scene], nextCursor: null },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    searchInput: '',
    setSearchInput: vi.fn(),
    status: '',
    setStatus: vi.fn(),
    owner: '',
    setOwner: vi.fn(),
    cursor: {
      cursor: null,
      page: 1,
      canGoBack: false,
      next: vi.fn(),
      previous: vi.fn(),
      reset: vi.fn(),
    },
    ...overrides,
  }
}

describe('TeamLabLibraryPage', () => {
  beforeEach(() => { vi.restoreAllMocks(); vi.mocked(useTeamLabCatalog).mockReturnValue(catalog()) })

  it('explains why a scene referenced by a game cannot be deleted', () => {
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)
    const row = screen.getByRole('row', { name: /企业域演练/ })
    expect(within(row).getByRole('button', { name: '删除场景 企业域演练' })).toBeDisabled()
    expect(within(row).getByText('请先解除比赛引用')).toBeInTheDocument()
  })

  it('requires explicit confirmation and keeps the dialog open on a server conflict', async () => {
    const remove = vi.spyOn(teamLabAdminApi, 'deleteTopology').mockRejectedValue(new RuntimeApiError('仍有课程引用，请先解除。', { kind: 'http', status: 409, code: 'topology_in_use' }))
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({ page: { items: [{ ...scene, gameReferenceCount: 0 }], nextCursor: null } }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)
    const row = screen.getByRole('row', { name: /企业域演练/ })
    fireEvent.click(within(row).getByRole('button', { name: '删除场景 企业域演练' }))
    const dialog = screen.getByRole('dialog', { name: '删除场景' })
    expect(dialog).toHaveTextContent('镜像模板、镜像文件及比赛数据保留')
    expect(remove).not.toHaveBeenCalled()
    fireEvent.click(within(dialog).getByRole('button', { name: '删除场景' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('仍有课程引用，请先解除。')
    expect(dialog).toHaveAttribute('open')
  })

  it('opens delete confirmation by keyboard without activating the surrounding table row', async () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({ page: { items: [{ ...scene, gameReferenceCount: 0 }], nextCursor: null } }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)
    const row = screen.getByRole('row', { name: /企业域演练/ })
    within(row).getByRole('button', { name: '删除场景 企业域演练' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(screen.getByRole('dialog', { name: '删除场景' })).toBeVisible()
  })

  it('renders the server-ordered scene projection', () => {
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByRole('heading', { name: '组网场景库' })).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /企业域演练/ })
    expect(row).toHaveTextContent('3 网段 · 8 资产')
    expect(row).not.toHaveTextContent('设施')
    expect(within(row).getByText('与最新发布一致')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: '版本与启动 企业域演练' })).toHaveAttribute('href', `/admin/teamlab/${scene.id}/releases`)
  })

  it('keeps complete scene actions in the narrow-screen item layout', () => {
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)
    const item = screen.getByRole('article')
    expect(within(item).getByRole('link', { name: '设计 企业域演练' })).toHaveAttribute('href', `/admin/teamlab/${scene.id}/design`)
    expect(within(item).getByRole('link', { name: '版本与启动 企业域演练' })).toHaveAttribute('href', `/admin/teamlab/${scene.id}/releases`)
    expect(item).toHaveTextContent('3 网段 · 8 资产')
  })

  it('makes an active trial explicit in the scene status', () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({
      page: {
        items: [{
          ...scene,
          latestTrialRuntime: {
            id: '019f0000-0000-7000-8000-000000000010',
            releaseId: scene.latestRelease!.id,
            status: 'planning',
            stage: 'planning',
            openForAccess: false,
            createdAt: scene.updatedAt,
            updatedAt: null,
            error: null,
          },
        }],
        nextCursor: null,
      },
    }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    const row = screen.getByRole('row', { name: /企业域演练/ })
    expect(within(row).getByText('与最新发布一致')).toBeInTheDocument()
    expect(within(row).getByText('规划中')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: '查看环境 企业域演练' })).toHaveAttribute('href', '/admin/teamlab/runtimes/019f0000-0000-7000-8000-000000000010')
  })

  it.each([
    ['cleanup-pending', '待清理'],
    ['destroying', '销毁中'],
  ] as const)('keeps the published lifecycle while the latest trial is %s', (status, runtimeLabel) => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({
      page: {
        items: [{
          ...scene,
          latestTrialRuntime: {
            id: '019f0000-0000-7000-8000-000000000010',
            releaseId: scene.latestRelease!.id,
            status,
            stage: status,
            openForAccess: false,
            createdAt: scene.updatedAt,
            updatedAt: null,
            error: null,
          },
        }],
        nextCursor: null,
      },
    }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    const row = screen.getByRole('row', { name: /企业域演练/ })
    expect(within(row).getByText('与最新发布一致')).toBeInTheDocument()
    expect(within(row).getByText(runtimeLabel)).toBeInTheDocument()
  })

  it('renders an explicit empty state without inventing local rows', () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({ page: { items: [], nextCursor: null } }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByText('没有匹配的组网场景')).toBeInTheDocument()
  })

  it('distinguishes permission failures from empty data', () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(
      catalog({ page: undefined, error: new RuntimeApiError('Forbidden', { kind: 'http', status: 403 }) })
    )
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByText('无法访问场景库')).toBeInTheDocument()
    expect(screen.queryByText('没有匹配的组网场景')).not.toBeInTheDocument()
  })
})
