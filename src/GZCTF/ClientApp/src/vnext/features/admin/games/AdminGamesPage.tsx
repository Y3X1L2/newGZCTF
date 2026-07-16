import { Eye, Plus, Search, Upload } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { GameInfoModel, GameType } from '@Api'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import {
  AdminDataColumn,
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import { GameCreateDialog } from './GameCreateDialog'
import { GameImportDialog } from './GameImportDialog'
import {
  gameLifecycle,
  gameLifecycleMeta,
  gameTypeLabel,
  type GameLifecycle,
} from './gamePresentation'
import { useAdminGames } from './useAdminGames'
import styles from './AdminGamesPage.module.css'

const PAGE_SIZE = 30

function searchableText(game: GameInfoModel) {
  return `${game.title} ${game.summary ?? ''} ${game.id ?? ''}`.toLocaleLowerCase('zh-CN')
}

export function AdminGamesPage() {
  const navigate = useNavigate()
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [createOpen, setCreateOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const gamesRequest = useAdminGames(queryState.page, PAGE_SIZE)
  const type = queryState.params.get('type') as GameType | null
  const status = queryState.params.get('status') as GameLifecycle | null

  useVNextPageTitle('赛事管理')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const rows = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').toLocaleLowerCase('zh-CN')
    return (gamesRequest.page?.items ?? []).filter((game) => {
      const lifecycle = gameLifecycle(game)
      return (!keyword || searchableText(game).includes(keyword)) && (!type || game.gameType === type) && (!status || lifecycle === status)
    })
  }, [gamesRequest.page?.items, queryState.params, status, type])

  const metrics = useMemo(() => {
    const source = gamesRequest.page?.items ?? []
    return {
      running: source.filter((game) => gameLifecycle(game) === 'running').length,
      scheduled: source.filter((game) => gameLifecycle(game) === 'scheduled').length,
      hidden: source.filter((game) => game.hidden).length,
    }
  }, [gamesRequest.page?.items])

  const columns: AdminDataColumn<GameInfoModel>[] = [
    {
      id: 'game',
      header: '比赛',
      width: 'wide',
      render: (game) => (
        <div className={styles.gameIdentity}>
          <strong>{game.title}</strong>
          <small>#{game.id ?? '—'} · {game.hidden ? '隐藏' : '公开'}</small>
        </div>
      ),
    },
    {
      id: 'type',
      header: '赛制',
      width: 'medium',
      render: (game) => gameTypeLabel(game.gameType),
    },
    {
      id: 'time',
      header: '时间',
      width: 'wide',
      visibility: 'desktop',
      render: (game) => (
        <div className={styles.timeCell}>
          <time>{formatAdminDate(game.start, false)}</time>
          <span>至 {formatAdminDate(game.end, false)}</span>
        </div>
      ),
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (game) => {
        const meta = gameLifecycleMeta(gameLifecycle(game))
        return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
      },
    },
    {
      id: 'rules',
      header: '规则',
      width: 'medium',
      visibility: 'wide',
      render: (game) => `${game.teamMemberCountLimit || '不限'} 人 · ${game.containerCountLimit ?? 0} 实例`,
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (game) => (
        <button aria-label={`管理 ${game.title}`} className={styles.iconButton} onClick={() => navigate(`/admin/games/${game.id}/info`)} type="button">
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const pageCount = Math.max(1, Math.ceil((gamesRequest.page?.total ?? 0) / PAGE_SIZE))
  const openCreated = (gameId: number) => {
    setCreateOpen(false)
    navigate(`/admin/games/${gameId}/info`)
  }
  const openImported = (gameId: number) => {
    setImportOpen(false)
    navigate(`/admin/games/${gameId}/info`)
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">创建比赛</ActionButton>
            <ActionButton icon={<Upload size={16} />} onClick={() => setImportOpen(true)} type="button">导入比赛</ActionButton>
          </>
        }
        description="创建、配置和检查各赛制比赛；专业赛制配置在比赛上下文中独立维护。"
        eyebrow="GAME OPERATIONS"
        title="赛事管理"
      />
      <MetricStrip>
        <MetricItem detail="服务器记录" label="比赛总数" value={gamesRequest.page?.total ?? 0} />
        <MetricItem detail="当前加载页" label="进行中" tone={metrics.running ? 'success' : 'neutral'} value={metrics.running} />
        <MetricItem detail="当前加载页" label="待开始" tone={metrics.scheduled ? 'info' : 'neutral'} value={metrics.scheduled} />
        <MetricItem detail="当前加载页" label="隐藏" value={metrics.hidden} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input aria-label="搜索当前页比赛" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="名称、摘要或编号" type="search" value={query} />
          </label>
          <select aria-label="筛选赛制" onChange={(event) => queryState.update({ type: event.currentTarget.value || null })} value={type ?? ''}>
            <option value="">全部赛制</option>
            {Object.values(GameType).map((value) => <option key={value} value={value}>{gameTypeLabel(value)}</option>)}
          </select>
          <select aria-label="筛选比赛状态" onChange={(event) => queryState.update({ status: event.currentTarget.value || null })} value={status ?? ''}>
            <option value="">全部状态</option>
            <option value="scheduled">未开始</option>
            <option value="running">进行中</option>
            <option value="ended">已结束</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={gamesRequest.isRefreshing} label="列表按需刷新" />
      </FilterToolbar>
      {gamesRequest.error ? <InlineFeedback tone="danger">{errorMessage(gamesRequest.error, '赛事列表加载失败。')}</InlineFeedback> : null}
      {gamesRequest.isLoading ? (
        <DataState description="正在读取赛事配置。" loading title="赛事列表加载中" />
      ) : (
        <>
          <DataTable caption="赛事管理列表" columns={columns} emptyDescription="当前加载页没有符合筛选条件的比赛。" emptyTitle="没有匹配比赛" onRowClick={(game) => navigate(`/admin/games/${game.id}/info`)} rowKey={(game) => game.id ?? game.title} rows={rows} />
          <PaginationBar onPageChange={queryState.setPage} page={queryState.page} pageCount={pageCount} total={gamesRequest.page?.total} />
        </>
      )}
      <GameCreateDialog onClose={() => setCreateOpen(false)} onCreated={openCreated} open={createOpen} />
      <GameImportDialog onClose={() => setImportOpen(false)} onImported={openImported} open={importOpen} />
    </div>
  )
}
