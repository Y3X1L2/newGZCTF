import { Eye, Plus, RefreshCw, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useOutletContext } from 'react-router'
import { ChallengeCategory, ChallengeInfoModel, ChallengeType, GameType } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { gameAdminApi } from '../../api'
import { useAdminImages } from '../../images/useAdminImages'
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
} from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { useAdminQueryState } from '../../shared/useAdminQueryState'
import type { GameAdminOutletContext } from '../GameAdminShell'
import {
  challengeCategoryLabel,
  challengeEnvironmentLabel,
  challengeTypeLabel,
  isContainerChallenge,
} from '../gamePresentation'
import { useAdminGameChallenges } from '../useAdminGames'
import { ChallengeCreateDialog } from './ChallengeCreateDialog'
import styles from './AdminGameChallengesPage.module.css'

const PAGE_SIZE = 30

function challengeSearchText(challenge: ChallengeInfoModel) {
  return `${challenge.title} ${challenge.id ?? ''} ${challenge.category ?? ''} ${challenge.type ?? ''}`.toLocaleLowerCase('zh-CN')
}

export function AdminGameChallengesPage() {
  const navigate = useNavigate()
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [createOpen, setCreateOpen] = useState(false)
  const [flushOpen, setFlushOpen] = useState(false)
  const [pendingIds, setPendingIds] = useState<Set<number>>(() => new Set())
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const challengesRequest = useAdminGameChallenges(gameId)
  const imagesRequest = useAdminImages({})
  const category = queryState.params.get('category') as ChallengeCategory | null
  const type = queryState.params.get('type') as ChallengeType | null
  const enabled = queryState.params.get('enabled')

  useVNextPageTitle(`${game.title} · CTF 题目`)

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const filtered = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').toLocaleLowerCase('zh-CN')
    return (challengesRequest.challenges ?? []).filter((challenge) =>
      (!keyword || challengeSearchText(challenge).includes(keyword)) &&
      (!category || challenge.category === category) &&
      (!type || challenge.type === type) &&
      (!enabled || challenge.isEnabled === (enabled === 'true'))
    )
  }, [category, challengesRequest.challenges, enabled, queryState.params, type])
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const rows = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  useEffect(() => {
    if (queryState.page <= pageCount) return
    queryState.update({ page: pageCount <= 1 ? null : pageCount }, { replace: true, resetPage: false })
  }, [pageCount, queryState])

  const metrics = useMemo(() => {
    const source = challengesRequest.challenges ?? []
    return {
      total: source.length,
      enabled: source.filter((challenge) => challenge.isEnabled).length,
      containers: source.filter((challenge) => isContainerChallenge(challenge.type)).length,
      attachments: source.filter((challenge) => !isContainerChallenge(challenge.type)).length,
    }
  }, [challengesRequest.challenges])

  const toggle = async (challenge: ChallengeInfoModel) => {
    if (!challenge.id || pendingIds.has(challenge.id)) return
    setPendingIds((current) => new Set(current).add(challenge.id as number))
    setFeedback(null)
    try {
      await gameAdminApi.updateChallenge(gameId, challenge.id, { isEnabled: !challenge.isEnabled })
      await challengesRequest.mutate()
      setFeedback({ tone: 'success', message: `${challenge.title} 已${challenge.isEnabled ? '停用' : '启用'}。` })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '题目状态更新失败。') })
    } finally {
      setPendingIds((current) => {
        const next = new Set(current)
        next.delete(challenge.id as number)
        return next
      })
    }
  }

  const flush = async () => {
    setFeedback(null)
    try {
      await gameAdminApi.flushScoreboard(gameId)
      setFeedback({ tone: 'success', message: '积分榜缓存已刷新，提交事实没有被修改。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '积分榜缓存刷新失败。') })
      return false
    }
  }

  const columns: AdminDataColumn<ChallengeInfoModel>[] = [
    {
      id: 'challenge',
      header: '题目',
      width: 'wide',
      render: (challenge) => <div className={styles.identity}><strong>{challenge.title}</strong><small>#{challenge.id ?? '—'} · {challengeCategoryLabel(challenge.category)}</small></div>,
    },
    { id: 'type', header: '类型', width: 'medium', render: (challenge) => challengeTypeLabel(challenge.type) },
    { id: 'environment', header: '环境', width: 'medium', visibility: 'desktop', render: (challenge) => challengeEnvironmentLabel(challenge.environment) },
    { id: 'score', header: '当前分值', width: 'compact', render: (challenge) => <span className={styles.mono}>{challenge.score ?? challenge.originalScore ?? '—'}</span> },
    { id: 'deadline', header: '截止时间', width: 'medium', visibility: 'wide', render: (challenge) => formatAdminDate(challenge.deadlineUtc, false) },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (challenge) => (
        <button className={styles.statusButton} disabled={!challenge.id || pendingIds.has(challenge.id)} onClick={() => void toggle(challenge)} type="button">
          <StatusBadge pulse={Boolean(challenge.id && pendingIds.has(challenge.id))} tone={challenge.isEnabled ? 'success' : 'neutral'}>{challenge.isEnabled ? '已启用' : '未启用'}</StatusBadge>
        </button>
      ),
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (challenge) => <button aria-label={`编辑 ${challenge.title}`} className={styles.iconButton} onClick={() => navigate(`/admin/games/${gameId}/challenges/${challenge.id}`)} type="button"><Eye size={16} /></button>,
    },
  ]

  if (game.gameType !== GameType.Jeopardy && game.gameType !== GameType.Mixed) {
    return <DataState description="当前赛制不使用 CTF 题目管理。" title="CTF 题目不可用" />
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">新建题目</ActionButton>
            <ActionButton icon={<RefreshCw size={16} />} onClick={() => setFlushOpen(true)} type="button">刷新积分榜</ActionButton>
          </>
        }
        description="维护 CTF 题目身份、环境和启用状态；附件、Flag 与测试实例在题目工作台中配置。"
        eyebrow="CTF CHALLENGES"
        title="题目管理"
      />
      <MetricStrip>
        <MetricItem detail="当前比赛" label="题目总数" value={metrics.total} />
        <MetricItem detail="选手可见" label="已启用" tone={metrics.enabled ? 'success' : 'neutral'} value={metrics.enabled} />
        <MetricItem detail="Docker / Windows" label="容器题" value={metrics.containers} />
        <MetricItem detail="静态 / 动态" label="附件题" value={metrics.attachments} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}><Search aria-hidden="true" size={16} /><input aria-label="搜索题目" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="名称、分类或编号" type="search" value={query} /></label>
          <select aria-label="筛选题目分类" onChange={(event) => queryState.update({ category: event.currentTarget.value || null })} value={category ?? ''}><option value="">全部分类</option>{Object.values(ChallengeCategory).map((value) => <option key={value} value={value}>{value}</option>)}</select>
          <select aria-label="筛选题目类型" onChange={(event) => queryState.update({ type: event.currentTarget.value || null })} value={type ?? ''}><option value="">全部类型</option>{Object.values(ChallengeType).map((value) => <option key={value} value={value}>{challengeTypeLabel(value)}</option>)}</select>
          <select aria-label="筛选启用状态" onChange={(event) => queryState.update({ enabled: event.currentTarget.value || null })} value={enabled ?? ''}><option value="">全部状态</option><option value="true">已启用</option><option value="false">未启用</option></select>
        </ToolbarGroup>
        <RefreshIndicator active={challengesRequest.isRefreshing} label="题目按需刷新" />
      </FilterToolbar>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {challengesRequest.error ? <InlineFeedback tone="danger">{errorMessage(challengesRequest.error, '题目列表加载失败。')}</InlineFeedback> : null}
      {challengesRequest.isLoading ? <DataState description="正在读取比赛题目。" loading title="题目列表加载中" /> : <><DataTable caption="CTF 题目管理列表" columns={columns} emptyDescription="当前条件下没有可展示的题目。" emptyTitle="没有匹配题目" onRowClick={(challenge) => navigate(`/admin/games/${gameId}/challenges/${challenge.id}`)} rowKey={(challenge) => challenge.id ?? challenge.title} rows={rows} /><PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} /></>}
      <ChallengeCreateDialog gameId={gameId} onClose={() => setCreateOpen(false)} onCreated={(challengeId) => { setCreateOpen(false); navigate(`/admin/games/${gameId}/challenges/${challengeId}`) }} open={createOpen} templates={imagesRequest.images ?? []} />
      <VNextConfirmDialog description="只清除积分榜缓存，不删除提交或分数事实。" message="刷新后积分榜会根据当前题目和提交重新读取数据。" onClose={() => setFlushOpen(false)} onConfirm={flush} open={flushOpen} title="刷新积分榜缓存？" />
    </div>
  )
}
