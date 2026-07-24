import { Pencil, Plus, Search, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import { GameNotice } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { gameOperationsAdminApi } from '../../api'
import { AdminDataColumn, AdminPageHeader, DataTable, FilterToolbar, MetricItem, MetricStrip, RefreshIndicator, ToolbarGroup } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { GameAdminOutletContext } from '../GameAdminShell'
import styles from '../GameOperations.module.css'
import { noticeContent, noticeSummary } from '../gameOperationsPresentation'
import { useAdminGameNotices } from '../useGameOperations'
import { GameNoticeDialog } from './GameNoticeDialog'

export function AdminGameNoticesPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const request = useAdminGameNotices(gameId)
  const [query, setQuery] = useState('')
  const [editorOpen, setEditorOpen] = useState(false)
  const [activeNotice, setActiveNotice] = useState<GameNotice | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<GameNotice | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const notices = useMemo(() => [...(request.notices ?? [])].sort((left, right) => right.time - left.time), [request.notices])
  const filtered = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return notices.filter((notice) => !keyword || noticeContent(notice).toLocaleLowerCase('zh-CN').includes(keyword) || notice.id.toString().includes(keyword))
  }, [notices, query])

  useVNextPageTitle(`${game.title} · 比赛公告`)

  const metrics = useMemo(() => ({
    total: notices.length,
    recent: notices.filter((notice) => notice.time >= Date.now() - 24 * 60 * 60_000).length,
    latest: notices[0]?.time ?? null,
  }), [notices])

  const openEditor = (notice: GameNotice | null) => {
    setActiveNotice(notice)
    setEditorOpen(true)
  }

  const remove = async () => {
    if (!deleteTarget) return false
    setFeedback(null)
    try {
      await gameOperationsAdminApi.removeNotice(gameId, deleteTarget.id)
      await request.mutate()
      setFeedback({ tone: 'success', message: '比赛公告已删除。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛公告删除失败。') })
      return false
    }
  }

  const columns: AdminDataColumn<GameNotice>[] = [
    { id: 'content', header: '公告内容', width: 'wide', render: (notice) => <div className={styles.identity}><strong className={styles.noticeText}>{noticeSummary(notice)}</strong><small>公告 #{notice.id}</small></div> },
    { id: 'time', header: '发布时间', width: 'medium', render: (notice) => <div className={styles.timeIdentity}><time>{formatAdminDate(notice.time, false)}</time><small>服务器记录</small></div> },
    { id: 'action', header: '操作', width: 'compact', align: 'right', render: (notice) => <span className={styles.rowActions}><button aria-label={`编辑公告 #${notice.id}`} className={styles.iconButton} onClick={() => openEditor(notice)} type="button"><Pencil size={16} /></button><button aria-label={`删除公告 #${notice.id}`} className={styles.iconButton} data-danger onClick={() => setDeleteTarget(notice)} type="button"><Trash2 size={16} /></button></span> },
  ]

  return (
    <div className={styles.page}>
      <AdminPageHeader actions={<ActionButton icon={<Plus size={16} />} onClick={() => openEditor(null)} tone="primary" type="button">发布公告</ActionButton>} description="维护选手可见的比赛公告；保存后由服务器负责实时推送。" eyebrow="GAME NOTICES" title="比赛公告" />
      <MetricStrip>
        <MetricItem detail="普通比赛公告" label="公告总数" value={metrics.total} />
        <MetricItem detail="最近 24 小时" label="近期发布" tone={metrics.recent ? 'info' : 'neutral'} value={metrics.recent} />
        <MetricItem detail="最近一次" label="最新发布" value={metrics.latest ? formatAdminDate(metrics.latest, false) : '—'} />
      </MetricStrip>
      <FilterToolbar><ToolbarGroup grow><label className={styles.searchBox}><Search aria-hidden="true" size={16} /><input aria-label="搜索比赛公告" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="公告内容或编号" type="search" value={query} /></label></ToolbarGroup><RefreshIndicator active={request.isRefreshing} label="公告按需刷新" /></FilterToolbar>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '比赛公告加载失败。')}</InlineFeedback> : null}
      {request.isLoading ? <DataState description="正在读取比赛公告。" loading title="公告加载中" /> : <DataTable caption="比赛公告管理列表" columns={columns} emptyDescription="发布公告后会显示在选手比赛工作区。" emptyTitle="尚未发布比赛公告" onRowClick={openEditor} rowKey={(notice) => notice.id} rows={filtered} />}
      <GameNoticeDialog gameId={gameId} notice={activeNotice} onClose={() => { setEditorOpen(false); setActiveNotice(null) }} onSaved={request.mutate} open={editorOpen} />
      <VNextConfirmDialog description="删除后选手端将不再返回该公告。" message={deleteTarget ? `将删除公告“${noticeSummary(deleteTarget, 80)}”。` : ''} onClose={() => setDeleteTarget(null)} onConfirm={remove} open={Boolean(deleteTarget)} title="删除比赛公告？" />
    </div>
  )
}
