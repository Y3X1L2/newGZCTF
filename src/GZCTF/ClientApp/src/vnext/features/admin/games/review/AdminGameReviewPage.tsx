import { Eye, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import { ParticipationEditModel, ParticipationInfoModel, ParticipationStatus } from '@Api'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { gameOperationsAdminApi } from '../../api'
import { AdminDataColumn, AdminPageHeader, DataTable, FilterToolbar, MetricItem, MetricStrip, PaginationBar, RefreshIndicator, StatusBadge, ToolbarGroup } from '../../shared/AdminWorkbench'
import { useAdminQueryState } from '../../shared/useAdminQueryState'
import type { GameAdminOutletContext } from '../GameAdminShell'
import styles from '../GameOperations.module.css'
import { participationSearchText, participationStatusMeta } from '../gameOperationsPresentation'
import { useAdminGameDivisions, useAdminGameParticipations } from '../useGameOperations'
import { ParticipationDetailDrawer } from './ParticipationDetailDrawer'

const PAGE_SIZE = 20

export function AdminGameReviewPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [selected, setSelected] = useState<ParticipationInfoModel | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const participationsRequest = useAdminGameParticipations(gameId)
  const divisionsRequest = useAdminGameDivisions(gameId)
  const status = queryState.params.get('status') as ParticipationStatus | null
  const divisionId = queryState.params.get('division')

  useVNextPageTitle(`${game.title} · 报名审核`)

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const filtered = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').toLocaleLowerCase('zh-CN')
    return (participationsRequest.participations ?? []).filter((participation) =>
      (!keyword || participationSearchText(participation).includes(keyword)) &&
      (!status || participation.status === status) &&
      (!divisionId || participation.divisionId?.toString() === divisionId)
    )
  }, [divisionId, participationsRequest.participations, queryState.params, status])
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const rows = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const divisionMap = useMemo(() => new Map((divisionsRequest.divisions ?? []).map((division) => [division.id, division.name])), [divisionsRequest.divisions])

  useEffect(() => {
    if (queryState.page <= pageCount) return
    queryState.update({ page: pageCount <= 1 ? null : pageCount }, { replace: true, resetPage: false })
  }, [pageCount, queryState])

  const metrics = useMemo(() => {
    const source = participationsRequest.participations ?? []
    return {
      total: source.length,
      pending: source.filter((item) => item.status === ParticipationStatus.Pending).length,
      accepted: source.filter((item) => item.status === ParticipationStatus.Accepted).length,
      blocked: source.filter((item) => item.status === ParticipationStatus.Rejected || item.status === ParticipationStatus.Suspended).length,
    }
  }, [participationsRequest.participations])

  const save = async (participationId: number, payload: ParticipationEditModel) => {
    setFeedback(null)
    try {
      await gameOperationsAdminApi.updateParticipation(participationId, payload)
      await participationsRequest.mutate()
      setFeedback({ tone: 'success', message: '报名状态已更新并从服务器重新读取。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '报名审核更新失败。') })
      return false
    }
  }

  const columns: AdminDataColumn<ParticipationInfoModel>[] = [
    { id: 'team', header: '战队', width: 'wide', render: (participation) => <div className={styles.teamIdentity}><strong>{participation.team.name ?? '未命名战队'}</strong><small>报名 #{participation.id} · 战队 #{participation.team.id ?? '—'}</small></div> },
    { id: 'members', header: '成员', width: 'compact', render: (participation) => <span className={styles.mono}>{participation.registeredMembers.length} / {participation.team.members?.length ?? 0}</span> },
    { id: 'division', header: '赛区', width: 'medium', visibility: 'desktop', render: (participation) => participation.divisionId ? divisionMap.get(participation.divisionId) ?? `未知赛区 #${participation.divisionId}` : '未分配' },
    { id: 'status', header: '状态', width: 'compact', render: (participation) => { const meta = participationStatusMeta(participation.status); return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge> } },
    { id: 'action', header: '操作', width: 'compact', align: 'right', render: (participation) => <button aria-label={`查看 ${participation.team.name ?? '战队'} 报名详情`} className={styles.iconButton} onClick={() => setSelected(participation)} type="button"><Eye size={16} /></button> },
  ]

  const loading = participationsRequest.isLoading || divisionsRequest.isLoading
  const loadError = participationsRequest.error || divisionsRequest.error

  return (
    <div className={styles.page}>
      <AdminPageHeader description="查看报名成员、分配赛区并处理待审核、暂停或拒绝状态。" eyebrow="PARTICIPATION REVIEW" title="报名审核" />
      <MetricStrip>
        <MetricItem detail="当前比赛" label="报名总数" value={metrics.total} />
        <MetricItem detail="需要处理" label="待审核" tone={metrics.pending ? 'warning' : 'neutral'} value={metrics.pending} />
        <MetricItem detail="有效参赛" label="已通过" tone={metrics.accepted ? 'success' : 'neutral'} value={metrics.accepted} />
        <MetricItem detail="拒绝或暂停" label="受限" tone={metrics.blocked ? 'danger' : 'neutral'} value={metrics.blocked} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}><Search aria-hidden="true" size={16} /><input aria-label="搜索报名战队或成员" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="战队、成员或报名编号" type="search" value={query} /></label>
          <select aria-label="筛选报名状态" onChange={(event) => queryState.update({ status: event.currentTarget.value || null })} value={status ?? ''}><option value="">全部状态</option>{Object.values(ParticipationStatus).map((value) => <option key={value} value={value}>{participationStatusMeta(value).label}</option>)}</select>
          <select aria-label="筛选报名赛区" onChange={(event) => queryState.update({ division: event.currentTarget.value || null })} value={divisionId ?? ''}><option value="">全部赛区</option>{(divisionsRequest.divisions ?? []).map((division) => <option key={division.id} value={division.id}>{division.name}</option>)}</select>
        </ToolbarGroup>
        <RefreshIndicator active={participationsRequest.isRefreshing || divisionsRequest.isRefreshing} label="审核数据按需刷新" />
      </FilterToolbar>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {loadError ? <InlineFeedback tone="danger">{errorMessage(loadError, '报名审核数据加载失败。')}</InlineFeedback> : null}
      {loading ? <DataState description="正在读取战队、成员和赛区。" loading title="报名数据加载中" /> : <><DataTable caption="比赛报名审核列表" columns={columns} emptyDescription="当前筛选条件下没有报名记录。" emptyTitle="没有匹配报名" onRowClick={setSelected} rowKey={(participation) => participation.id} rows={rows} /><PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} /></>}
      <ParticipationDetailDrawer divisions={divisionsRequest.divisions ?? []} onClose={() => setSelected(null)} onSave={save} open={Boolean(selected)} participation={selected} />
    </div>
  )
}
