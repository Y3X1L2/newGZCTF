import { Calculator, RefreshCw, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import {
  GameType,
  TheoryAnswerSheetStatus,
  TheoryAnswerSheetSummaryModel,
  TheoryScoreboardItemModel,
} from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { theoryPaperTotalScore } from '../../theory/paperModel'
import { theoryAdminApi } from '../api'
import type { GameAdminOutletContext } from '../games/GameAdminShell'
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
import { useTheoryPaper, useTheoryResults } from './useTheoryAdmin'
import styles from './AdminTheoryResultsPage.module.css'

const PAGE_SIZE = 30

function normalizedSearch(...values: Array<number | string | null | undefined>) {
  return values.filter((value) => value !== null && value !== undefined).join(' ').toLocaleLowerCase('zh-CN')
}

function answerSheetStatusLabel(status: TheoryAnswerSheetStatus | undefined) {
  return status === TheoryAnswerSheetStatus.Submitted ? '已提交' : '草稿'
}

export function AdminTheoryResultsPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const request = useTheoryResults(gameId)
  const paperRequest = useTheoryPaper(gameId)
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [recalculateOpen, setRecalculateOpen] = useState(false)
  const [recalculating, setRecalculating] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const status = queryState.params.get('status') as TheoryAnswerSheetStatus | null

  useVNextPageTitle(`${game.title} · 理论成绩`)

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(
      () => queryState.update({ q: query.trim() || null }, { replace: true }),
      250
    )
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const results = request.results
  const submissions = results?.submissions ?? []
  const scoreboard = results?.scoreboard ?? []
  const keyword = (queryState.params.get('q') ?? '').toLocaleLowerCase('zh-CN')
  const filteredScoreboard = useMemo(
    () => scoreboard.filter((item) => !keyword || normalizedSearch(item.teamId, item.teamName, item.userName).includes(keyword)),
    [keyword, scoreboard]
  )
  const filteredSubmissions = useMemo(
    () => submissions.filter((item) => {
      if (status && item.status !== status) return false
      return !keyword || normalizedSearch(item.id, item.teamId, item.teamName, item.userId, item.userName).includes(keyword)
    }),
    [keyword, status, submissions]
  )
  const pageCount = Math.max(1, Math.ceil(filteredSubmissions.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const submissionRows = filteredSubmissions.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const submittedCount = submissions.filter((item) => item.status === TheoryAnswerSheetStatus.Submitted).length
  const draftCount = submissions.length - submittedCount
  const paperMaximumScore = paperRequest.paper ? theoryPaperTotalScore(paperRequest.paper) : 0
  const maximumScore = scoreboard.reduce((maximum, item) => Math.max(maximum, item.maxScore ?? 0), paperMaximumScore)

  useEffect(() => {
    if (queryState.page <= pageCount) return
    queryState.update({ page: pageCount <= 1 ? null : pageCount }, { replace: true, resetPage: false })
  }, [pageCount, queryState])

  const refresh = async () => {
    setFeedback(null)
    try {
      await Promise.all([request.mutate(), paperRequest.mutate()])
      setFeedback({ tone: 'success', message: '理论成绩已从服务器重新读取。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论成绩刷新失败。') })
    }
  }

  const recalculate = async () => {
    if (recalculating) return false
    setRecalculating(true)
    setFeedback(null)
    try {
      await theoryAdminApi.recalculateResults(gameId)
      await request.mutate()
      setFeedback({ tone: 'success', message: '已重新判定所有已提交答卷，并从服务器回读最新成绩。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论成绩重新判定失败。') })
      return false
    } finally {
      setRecalculating(false)
    }
  }

  const scoreboardColumns: AdminDataColumn<TheoryScoreboardItemModel>[] = [
    { id: 'rank', header: '排名', width: 'compact', render: (item) => <strong className={styles.rank}>#{item.rank ?? '—'}</strong> },
    {
      id: 'team',
      header: '队伍',
      width: 'wide',
      render: (item) => <div className={styles.identity}><strong>{item.teamName || '未命名队伍'}</strong><small>队伍 #{item.teamId ?? '—'}</small></div>,
    },
    { id: 'member', header: '最高分成员', width: 'medium', render: (item) => item.userName || '尚未提交' },
    { id: 'division', header: '赛区', width: 'compact', visibility: 'wide', render: (item) => item.divisionId ? `#${item.divisionId}` : '—' },
    {
      id: 'score',
      header: '理论成绩',
      width: 'compact',
      render: (item) => <strong className={styles.score}>{item.score ?? 0} / {item.maxScore ?? maximumScore}</strong>,
    },
    { id: 'submitted', header: '最佳提交时间', width: 'medium', visibility: 'desktop', render: (item) => formatAdminDate(item.submittedAt) },
  ]

  const submissionColumns: AdminDataColumn<TheoryAnswerSheetSummaryModel>[] = [
    {
      id: 'identity',
      header: '答卷',
      width: 'wide',
      render: (item) => <div className={styles.identity}><strong>{item.userName || '未知用户'}</strong><small>{item.teamName || '未命名队伍'} · 答卷 #{item.id ?? '—'}</small></div>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (item) => <StatusBadge tone={item.status === TheoryAnswerSheetStatus.Submitted ? 'success' : 'info'}>{answerSheetStatusLabel(item.status)}</StatusBadge>,
    },
    {
      id: 'score',
      header: '得分',
      width: 'compact',
      render: (item) => item.status === TheoryAnswerSheetStatus.Submitted ? <strong className={styles.score}>{item.score ?? 0} / {item.maxScore ?? maximumScore}</strong> : '—',
    },
    { id: 'updated', header: '最近保存', width: 'medium', visibility: 'desktop', render: (item) => formatAdminDate(item.updatedAt) },
    { id: 'submitted', header: '最终提交', width: 'medium', visibility: 'wide', render: (item) => formatAdminDate(item.submittedAt) },
  ]

  if (game.gameType !== GameType.Theory && game.gameType !== GameType.Mixed) {
    return <DataState description="当前赛制不产生理论答卷和理论成绩。" title="理论成绩不可用" />
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton disabled={request.isRefreshing || paperRequest.isRefreshing || recalculating} icon={<RefreshCw size={16} />} onClick={() => void refresh()} type="button">刷新</ActionButton>
            <ActionButton disabled={recalculating} icon={<Calculator size={16} />} onClick={() => setRecalculateOpen(true)} tone="primary" type="button">重新判分</ActionButton>
          </>
        }
        description="查看队伍最高分排名与个人答卷摘要；理论成绩与实战积分保持独立。"
        eyebrow="THEORY RESULTS"
        title="理论成绩"
      />
      <MetricStrip density="comfortable">
        <MetricItem detail="已审核参赛队伍" label="榜单队伍" value={scoreboard.length} />
        <MetricItem detail="最终提交不可修改" label="已提交答卷" tone={submittedCount ? 'success' : 'neutral'} value={submittedCount} />
        <MetricItem detail="尚未计入队伍成绩" label="草稿答卷" tone={draftCount ? 'warning' : 'neutral'} value={draftCount} />
        <MetricItem detail="一队取成员最高分" label="试卷满分" value={maximumScore || '—'} />
      </MetricStrip>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '理论成绩加载失败。')}</InlineFeedback> : null}
      {paperRequest.error ? <InlineFeedback tone="danger">{errorMessage(paperRequest.error, '试卷满分读取失败。')}</InlineFeedback> : null}
      {request.isLoading && !results ? (
        <DataState description="正在读取队伍榜单和个人答卷。" loading title="理论成绩加载中" />
      ) : (
        <>
          <section className={styles.section}>
            <header className={styles.sectionHeader}>
              <div><span>TEAM SCOREBOARD</span><h2>队伍榜单</h2></div>
              <p>每支队伍只计入成员最终提交中的最高分；同分时较早提交优先。</p>
            </header>
            <DataTable
              caption="理论考试队伍榜单"
              columns={scoreboardColumns}
              density="comfortable"
              emptyDescription={keyword ? '当前搜索条件没有匹配队伍。' : '暂无已审核参赛队伍。'}
              emptyTitle="没有榜单数据"
              rowKey={(item) => item.teamId ?? item.teamName ?? 'unknown-team'}
              rows={filteredScoreboard}
            />
          </section>
          <section className={styles.section}>
            <header className={styles.sectionHeader}>
              <div><span>ANSWER SHEETS</span><h2>个人答卷</h2></div>
              <p>当前接口仅提供答卷摘要；逐题作答详情与导出能力暂缓，页面不提供无效入口。</p>
            </header>
            <FilterToolbar>
              <ToolbarGroup grow>
                <label className={styles.searchBox}>
                  <Search aria-hidden="true" size={16} />
                  <input aria-label="搜索理论答卷" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="队伍、用户或答卷编号" type="search" value={query} />
                </label>
                <select aria-label="筛选答卷状态" onChange={(event) => queryState.update({ status: event.currentTarget.value || null })} value={status ?? ''}>
                  <option value="">全部状态</option>
                  <option value={TheoryAnswerSheetStatus.Draft}>草稿</option>
                  <option value={TheoryAnswerSheetStatus.Submitted}>已提交</option>
                </select>
              </ToolbarGroup>
              <RefreshIndicator active={request.isRefreshing || paperRequest.isRefreshing || recalculating} label="成绩按需刷新" />
            </FilterToolbar>
            <DataTable
              caption="理论考试个人答卷摘要"
              columns={submissionColumns}
              density="comfortable"
              emptyDescription="当前条件下没有个人答卷。"
              emptyTitle="没有匹配答卷"
              rowKey={(item) => item.id ?? `${item.userId}-${item.participationId}`}
              rows={submissionRows}
            />
            <PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filteredSubmissions.length} />
          </section>
        </>
      )}
      <VNextConfirmDialog
        confirmLabel="确认重新判分"
        description="该操作会按当前试卷快照重新判定所有已提交答卷。"
        message="草稿不会被判分；完成后页面会再次从服务器读取榜单和答卷摘要。"
        onClose={() => setRecalculateOpen(false)}
        onConfirm={recalculate}
        open={recalculateOpen}
        title="重新判定理论成绩？"
        tone="primary"
      />
    </div>
  )
}
