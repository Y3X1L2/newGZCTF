import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { AdminGamePhase, gameOperationsAdminApi } from '../../api'
import {
  AdminDataColumn,
  AdminPageHeader,
  DataTable,
  MetricItem,
  MetricStrip,
  StatusBadge,
} from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { GameAdminOutletContext } from '../GameAdminShell'
import styles from '../GameOperations.module.css'
import { useAdminGamePhases } from '../useGameOperations'
import { PhaseEditorDialog } from './PhaseEditorDialog'
import { phaseDurationLabel, phaseLifecycle } from './phaseModel'

function phaseStatusMeta(phase: AdminGamePhase) {
  const status = phaseLifecycle(phase)
  if (status === 'active') return { label: '进行中', tone: 'success' as const }
  if (status === 'scheduled') return { label: '未开始', tone: 'info' as const }
  return { label: '已结束', tone: 'neutral' as const }
}

export function AdminGamePhasesPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const request = useAdminGamePhases(gameId)
  const [editorOpen, setEditorOpen] = useState(false)
  const [activePhase, setActivePhase] = useState<AdminGamePhase | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AdminGamePhase | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const phases = useMemo(() => [...(request.phases ?? [])].sort((left, right) => left.startTime - right.startTime), [request.phases])

  useVNextPageTitle(`${game.title} · 比赛阶段`)

  const metrics = useMemo(() => ({
    total: phases.length,
    active: phases.filter((phase) => phaseLifecycle(phase) === 'active').length,
    scheduled: phases.filter((phase) => phaseLifecycle(phase) === 'scheduled').length,
    ctfDisabled: phases.filter((phase) => !phase.ctfEnabled).length,
  }), [phases])

  const openEditor = (phase: AdminGamePhase | null) => {
    setActivePhase(phase)
    setEditorOpen(true)
  }

  const remove = async () => {
    if (!deleteTarget) return false
    setFeedback(null)
    try {
      await gameOperationsAdminApi.removePhase(deleteTarget.id)
      await request.mutate()
      setFeedback({ tone: 'success', message: `阶段“${deleteTarget.name}”已删除。` })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '阶段删除失败。') })
      return false
    }
  }

  const columns: AdminDataColumn<AdminGamePhase>[] = [
    { id: 'name', header: '阶段', width: 'wide', render: (phase) => <div className={styles.identity}><strong>{phase.name}</strong><small>#{phase.id} · {phaseDurationLabel(phase)}</small></div> },
    { id: 'start', header: '开始', width: 'medium', render: (phase) => <div className={styles.timeIdentity}><time>{formatAdminDate(phase.startTime, false)}</time><small>本地时间</small></div> },
    { id: 'end', header: '结束', width: 'medium', visibility: 'desktop', render: (phase) => <div className={styles.timeIdentity}><time>{formatAdminDate(phase.endTime, false)}</time><small>本地时间</small></div> },
    { id: 'ctf', header: 'CTF', width: 'compact', render: (phase) => <StatusBadge tone={phase.ctfEnabled ? 'success' : 'neutral'}>{phase.ctfEnabled ? '启用' : '关闭'}</StatusBadge> },
    { id: 'status', header: '状态', width: 'compact', render: (phase) => { const meta = phaseStatusMeta(phase); return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge> } },
    { id: 'action', header: '操作', width: 'compact', align: 'right', render: (phase) => <span className={styles.rowActions}><button aria-label={`编辑 ${phase.name}`} className={styles.iconButton} onClick={() => openEditor(phase)} type="button"><Pencil size={16} /></button><button aria-label={`删除 ${phase.name}`} className={styles.iconButton} data-danger onClick={() => setDeleteTarget(phase)} type="button"><Trash2 size={16} /></button></span> },
  ]

  return (
    <div className={styles.page}>
      <AdminPageHeader actions={<ActionButton icon={<Plus size={16} />} onClick={() => openEditor(null)} tone="primary" type="button">新建阶段</ActionButton>} description="按时间维护比赛阶段和 CTF 操作开关；其他赛制阶段仍由各自模块管理。" eyebrow="GAME PHASES" title="比赛阶段" />
      <MetricStrip>
        <MetricItem detail="当前比赛" label="阶段总数" value={metrics.total} />
        <MetricItem detail="当前时刻" label="进行中" tone={metrics.active ? 'success' : 'neutral'} value={metrics.active} />
        <MetricItem detail="尚未开始" label="待执行" tone={metrics.scheduled ? 'info' : 'neutral'} value={metrics.scheduled} />
        <MetricItem detail="阶段级限制" label="关闭 CTF" tone={metrics.ctfDisabled ? 'warning' : 'neutral'} value={metrics.ctfDisabled} />
      </MetricStrip>
      <InlineFeedback>当前后端阶段模型仅保存 CTF 开关；理论考试、AWDP 和渗透模块不会由此页面伪造阶段状态。</InlineFeedback>
      {phases.length ? <ol aria-label="比赛阶段时间轴" className={styles.timeline}>{phases.map((phase) => <li className={styles.timelineItem} data-state={phaseLifecycle(phase)} key={phase.id}><strong>{phase.name}</strong><small>{formatAdminDate(phase.startTime, false)}</small><StatusBadge tone={phaseStatusMeta(phase).tone}>{phaseStatusMeta(phase).label}</StatusBadge></li>)}</ol> : null}
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '比赛阶段加载失败。')}</InlineFeedback> : null}
      {request.isLoading ? <DataState description="正在读取比赛阶段。" loading title="阶段加载中" /> : <DataTable caption="比赛阶段管理列表" columns={columns} emptyDescription="创建阶段后可按时间控制 CTF 操作。" emptyTitle="尚未配置比赛阶段" onRowClick={openEditor} rowKey={(phase) => phase.id} rows={phases} />}
      <PhaseEditorDialog game={game} onClose={() => { setEditorOpen(false); setActivePhase(null) }} onSaved={request.mutate} open={editorOpen} phase={activePhase} phases={phases} />
      <VNextConfirmDialog confirmationText={deleteTarget?.name} description={deleteTarget && phaseLifecycle(deleteTarget) === 'active' ? '该阶段当前正在进行，删除后无法恢复。' : '删除后无法恢复。'} message={deleteTarget ? `将永久删除阶段“${deleteTarget.name}”。` : ''} onClose={() => setDeleteTarget(null)} onConfirm={remove} open={Boolean(deleteTarget)} title="删除比赛阶段？" />
    </div>
  )
}
