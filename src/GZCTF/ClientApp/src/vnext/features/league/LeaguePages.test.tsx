import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LeagueMatchDetail, ProfileUserInfoModel } from '@Api'
import { LeagueCreatePage, LeagueDetailPage, LeagueListPage } from './LeaguePages'
import { leagueApi } from './api/leagueApi'
import { leagueCatalogApi } from './api/leagueCatalogApi'

const mocks = vi.hoisted(() => ({
  user: { userId: 'admin', role: 'Admin' } as ProfileUserInfoModel | undefined,
  error: undefined as unknown,
  enabled: true,
}))
vi.mock('../account/useCurrentAccount', () => ({
  useCurrentAccount: () => ({
    user: mocks.user,
    error: mocks.error,
    isAuthenticated: Boolean(mocks.user),
    isAdmin: mocks.user?.role === 'Admin',
    mutate: vi.fn(),
  }),
}))
vi.mock('./leagueFeature', () => ({
  get leagueEnabled() {
    return mocks.enabled
  },
}))
vi.mock('../../shared/useVNextPageTitle', () => ({ useVNextPageTitle: vi.fn() }))
vi.mock('./api/leagueApi', () => ({
  leagueApi: {
    list: vi.fn(),
    detail: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    register: vi.fn(),
    review: vi.fn(),
    selectTeams: vi.fn(),
  },
}))
vi.mock('./api/leagueCatalogApi', () => ({ leagueCatalogApi: { teams: vi.fn(), scenes: vi.fn(), releases: vi.fn() } }))

const matchId = '10000000-0000-4000-8000-000000000001'
const topologyId = '20000000-0000-4000-8000-000000000001'
const releaseId = '20000000-0000-4000-8000-000000000002'
let detail: LeagueMatchDetail

function mount(path = `/league/${matchId}`) {
  const cache = new Map()
  const router = createMemoryRouter(
    [
      { path: '/league', element: <LeagueListPage /> },
      { path: '/league/new', element: <LeagueCreatePage /> },
      { path: '/league/:matchId', element: <LeagueDetailPage /> },
    ],
    { initialEntries: [path] }
  )
  return render(
    <SWRConfig value={{ provider: () => cache, dedupingInterval: 0 }}>
      <RouterProvider router={router} />
    </SWRConfig>
  )
}

beforeEach(() => {
  vi.resetAllMocks()
  mocks.enabled = true
  mocks.user = { userId: 'admin', role: 'Admin' as ProfileUserInfoModel['role'] }
  mocks.error = undefined
  detail = {
    match: { id: matchId, name: '测试联赛', state: 0, revision: 4, createdAt: 1790000000000 },
    initialCoins: 100,
    topologyId: null,
    releaseId: null,
    registrations: [
      { teamId: 11, teamName: '甲队', state: 0, selected: false, seat: null },
      { teamId: 22, teamName: '乙队', state: 1, selected: false, seat: null },
    ],
    allowedActions: ['register', 'edit', 'review', 'selectTeams', 'prepare', 'abort'],
  }
  vi.mocked(leagueApi.detail).mockImplementation(async () => structuredClone(detail))
  vi.mocked(leagueApi.list).mockImplementation(async () => ({ items: [detail.match!], nextCursor: null }))
  vi.mocked(leagueCatalogApi.teams).mockResolvedValue([])
  vi.mocked(leagueCatalogApi.scenes).mockResolvedValue({
    items: [{ id: topologyId, name: '固定场景' } as never],
    nextCursor: null,
  })
  vi.mocked(leagueCatalogApi.releases).mockResolvedValue([
    { id: releaseId, version: 3, publishedAt: 1790000000000 } as never,
  ])
})

describe('League U1 workflow', () => {
  it('creates a draft with a real selected release and opens its returned identity', async () => {
    vi.mocked(leagueApi.create).mockResolvedValue(detail)
    const user = userEvent.setup()
    mount('/league/new')
    await user.type(await screen.findByRole('textbox', { name: /场次名称/ }), '新联赛')
    await user.clear(screen.getByRole('spinbutton', { name: /初始金币/ }))
    await user.type(screen.getByRole('spinbutton', { name: /初始金币/ }), '50')
    await screen.findByRole('option', { name: '固定场景' })
    await user.selectOptions(screen.getByLabelText('固定场景'), topologyId)
    await screen.findByRole('option', { name: /版本 3/ })
    await user.selectOptions(screen.getByLabelText('发布版本'), releaseId)
    await user.click(screen.getByRole('button', { name: '创建场次' }))
    await screen.findByRole('heading', { name: '测试联赛', level: 1 })
    expect(leagueApi.create).toHaveBeenCalledExactlyOnceWith({
      name: '新联赛',
      initialCoins: 50,
      topologyId,
      releaseId,
    })
  })

  it('keeps edits after revision conflict and requires explicit reload before another save', async () => {
    vi.mocked(leagueApi.update).mockImplementation(async () => {
      detail = { ...detail, match: { ...detail.match, name: '他人修改', revision: 5 } }
      throw { response: { status: 409, data: { code: 'league_revision_conflict' } } }
    })
    const user = userEvent.setup()
    mount()
    const name = await screen.findByRole('textbox', { name: /场次名称/ })
    await user.clear(name)
    await user.type(name, '保留我的输入')
    await user.click(screen.getByRole('button', { name: '保存配置' }))
    await screen.findByText(/你的输入已保留/)
    expect(name).toHaveValue('保留我的输入')
    expect(screen.getByRole('button', { name: '保存配置' })).toBeDisabled()
    expect(leagueApi.update).toHaveBeenCalledWith(matchId, 4, expect.objectContaining({ name: '保留我的输入' }))
    await user.click(screen.getByRole('button', { name: '放弃输入，载入最新配置' }))
    expect(name).toHaveValue('他人修改')
    expect(screen.getByRole('button', { name: '保存配置' })).toBeEnabled()
  })

  it('reviews before selecting two distinct seats, preserving the chosen order and latest revision', async () => {
    vi.mocked(leagueApi.review).mockImplementation(async () => {
      detail = {
        ...detail,
        match: { ...detail.match, revision: 5 },
        registrations: detail.registrations!.map((r) => ({ ...r, state: 1 })),
      }
      return detail
    })
    vi.mocked(leagueApi.selectTeams).mockImplementation(async (_id, revision, first, second) => ({
      ...detail,
      match: { ...detail.match, revision: revision + 1 },
      registrations: detail.registrations!.map((r) => ({
        ...r,
        selected: true,
        seat: r.teamId === first ? 1 : r.teamId === second ? 2 : null,
      })),
    }))
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: '通过 甲队' }))
    await waitFor(() => expect(screen.getByRole('button', { name: '通过 甲队' })).toBeDisabled())
    await user.selectOptions(screen.getByLabelText(/席位 1/), '22')
    await user.selectOptions(screen.getByLabelText(/席位 2/), '22')
    expect(screen.getByRole('button', { name: '保存双队席位' })).toBeDisabled()
    await user.selectOptions(screen.getByLabelText(/席位 2/), '11')
    await user.click(screen.getByRole('button', { name: '保存双队席位' }))
    expect(leagueApi.selectTeams).toHaveBeenCalledExactlyOnceWith(matchId, 5, 22, 11)
    await screen.findByText('战队 #22 · 席位 1')
    expect(screen.queryByText(/所选队伍保留/)).not.toBeInTheDocument()
  })

  it('lets only an unlocked captain team register and hides administrative controls', async () => {
    mocks.user = { userId: 'captain', role: 'Student' as ProfileUserInfoModel['role'] }
    detail.allowedActions = ['register']
    detail.registrations = []
    vi.mocked(leagueCatalogApi.teams).mockResolvedValue([
      { id: 11, name: '可报名', locked: false, members: [{ id: 'captain', captain: true }] },
      { id: 22, name: '已锁定', locked: true, members: [{ id: 'captain', captain: true }] },
      { id: 33, name: '非队长', locked: false, members: [{ id: 'captain', captain: false }] },
    ])
    vi.mocked(leagueApi.register).mockResolvedValue({
      ...detail,
      registrations: [{ teamId: 11, teamName: '可报名', state: 0 }],
    })
    const user = userEvent.setup()
    mount()
    await screen.findByRole('option', { name: '可报名' })
    expect(screen.queryByRole('option', { name: '已锁定' })).not.toBeInTheDocument()
    expect(screen.queryByRole('option', { name: '非队长' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '保存配置' })).not.toBeInTheDocument()
    expect(leagueCatalogApi.scenes).not.toHaveBeenCalled()
    await user.selectOptions(screen.getByLabelText(/报名战队/), '11')
    await user.click(screen.getByRole('button', { name: '提交报名' }))
    await screen.findByText('待审核')
    expect(leagueApi.register).toHaveBeenCalledExactlyOnceWith(matchId, 11)
    expect(screen.queryByRole('button', { name: '提交报名' })).not.toBeInTheDocument()
  })

  it('disables repeated writes while a request is pending', async () => {
    let finish!: (value: LeagueMatchDetail) => void
    vi.mocked(leagueApi.review).mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        })
    )
    mount()
    const button = await screen.findByRole('button', { name: '通过 甲队' })
    fireEvent.click(button)
    fireEvent.click(button)
    expect(leagueApi.review).toHaveBeenCalledTimes(1)
    expect(button).toBeDisabled()
    await act(async () => finish(detail))
  })

  it('shows the backend-disabled state without offering creation', async () => {
    vi.mocked(leagueApi.list).mockRejectedValue({ response: { status: 503, data: { code: 'league_disabled' } } })
    mount('/league')
    await screen.findByText('联赛尚未开放，请联系管理员。')
    expect(screen.queryByRole('link', { name: /创建场次/ })).not.toBeInTheDocument()
  })

  it('keeps production entry closed and does not query League until enabled', async () => {
    mocks.enabled = false
    mount('/league')
    expect(screen.getByText('联赛尚未开放')).toBeVisible()
    expect(leagueApi.list).not.toHaveBeenCalled()
  })

  it('preserves cursor navigation when returning from a match', async () => {
    const second = '10000000-0000-4000-8000-000000000002'
    vi.mocked(leagueApi.list).mockImplementation(async (after) =>
      after
        ? { items: [{ ...detail.match, id: second, name: '第二页场次' }], nextCursor: null }
        : { items: [detail.match!], nextCursor: matchId }
    )
    vi.mocked(leagueApi.detail).mockResolvedValue({
      ...detail,
      match: { ...detail.match, id: second, name: '第二页场次' },
    })
    const user = userEvent.setup()
    mount('/league')
    await screen.findByRole('link', { name: /测试联赛/ })
    await user.click(screen.getByRole('button', { name: '下一页' }))
    await user.click(await screen.findByRole('link', { name: /第二页场次/ }))
    await screen.findByRole('heading', { level: 1, name: '第二页场次' })
    await user.click(screen.getByRole('link', { name: '联赛列表' }))
    expect(await screen.findByText('第 2 页')).toBeVisible()
    expect(leagueApi.list).toHaveBeenCalledWith(matchId)
    const pagination = screen.getByRole('navigation', { name: '联赛分页' })
    await user.click(within(pagination).getByRole('button', { name: '上一页' }))
    expect(await screen.findByText('第 1 页')).toBeVisible()
  })

  it('does not request invalid detail IDs and directs anonymous users to login', () => {
    const page = mount('/league/not-a-uuid')
    expect(screen.getByText('场次地址无效')).toBeVisible()
    expect(leagueApi.detail).not.toHaveBeenCalled()
    page.unmount()
    mocks.user = undefined
    mocks.error = { response: { status: 401 } }
    mount('/league')
    expect(screen.getByRole('link', { name: '登录平台' })).toHaveAttribute('href', '/account/login')
    expect(leagueApi.list).not.toHaveBeenCalled()
  })
  it('rejects fractional coins and a scene without a release before any write', async () => {
    const user = userEvent.setup()
    mount('/league/new')
    const name = await screen.findByRole('textbox', { name: /场次名称/ })
    await user.type(name, '有效场次')
    const coins = screen.getByRole('spinbutton', { name: /初始金币/ })
    fireEvent.change(coins, { target: { value: '1.5' } })
    fireEvent.submit(name.closest('form')!)
    expect(await screen.findByText(/金币须为/)).toBeVisible()
    expect(leagueApi.create).not.toHaveBeenCalled()
    fireEvent.change(coins, { target: { value: '0' } })
    await user.selectOptions(screen.getByLabelText('固定场景'), topologyId)
    fireEvent.submit(name.closest('form')!)
    expect(await screen.findByText(/请选择该场景的发布版本/)).toBeVisible()
    expect(leagueApi.create).not.toHaveBeenCalled()
  })

  it('removes editing controls after refresh reports that preparation froze the configuration', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByRole('button', { name: '保存配置' })
    detail = { ...detail, match: { ...detail.match, state: 1, revision: 5 }, allowedActions: ['abort'] }
    await user.click(screen.getByRole('button', { name: '刷新详情' }))
    await screen.findByText('准备中')
    expect(screen.queryByRole('button', { name: '保存配置' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '通过 甲队' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '保存双队席位' })).not.toBeInTheDocument()
  })

  it('protects an unsaved configuration when leaving through the list link', async () => {
    const user = userEvent.setup()
    mount()
    await user.type(await screen.findByRole('textbox', { name: /场次名称/ }), '未保存')
    await user.click(screen.getByRole('link', { name: '联赛列表' }))
    await screen.findByRole('dialog', { name: '离开未保存的配置？' })
    await user.click(screen.getByRole('button', { name: '放弃修改并离开' }))
    await screen.findByRole('heading', { level: 1, name: '数字攻防联赛' })
    expect(leagueApi.update).not.toHaveBeenCalled()
  })
})
