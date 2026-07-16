import { Copy, Pencil, Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import { Division, GamePermission, GameType } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { gameOperationsAdminApi } from '../../api'
import { AdminDataColumn, AdminPageHeader, DataTable, MetricItem, MetricStrip, StatusBadge } from '../../shared/AdminWorkbench'
import type { GameAdminOutletContext } from '../GameAdminShell'
import styles from '../GameOperations.module.css'
import { useAdminGameChallenges } from '../useAdminGames'
import { useAdminGameDivisions } from '../useGameOperations'
import { DivisionEditorDrawer } from './DivisionEditorDrawer'
import { divisionPermissionSummary, hasGamePermission } from './divisionModel'

export function AdminGameDivisionsPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const divisionsRequest = useAdminGameDivisions(gameId)
  const ctf = game.gameType === GameType.Jeopardy || game.gameType === GameType.Mixed
  const challengesRequest = useAdminGameChallenges(ctf ? gameId : 0)
  const [editorOpen, setEditorOpen] = useState(false)
  const [activeDivision, setActiveDivision] = useState<Division | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Division | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const divisions = useMemo(() => [...(divisionsRequest.divisions ?? [])].sort((left, right) => left.name.localeCompare(right.name, 'zh-CN')), [divisionsRequest.divisions])

  useVNextPageTitle(`${game.title} · 赛区管理`)

  const metrics = useMemo(() => ({
    total: divisions.length,
    review: divisions.filter((division) => hasGamePermission(division.defaultPermissions, GamePermission.RequireReview)).length,
    invite: divisions.filter((division) => Boolean(division.inviteCode)).length,
    overrides: divisions.reduce((total, division) => total + (division.challengeConfigs?.length ?? 0), 0),
  }), [divisions])

  const openEditor = (division: Division | null) => {
    setActiveDivision(division)
    setEditorOpen(true)
  }

  const copyInviteCode = async (division: Division) => {
    if (!division.inviteCode) return
    try {
      await navigator.clipboard.writeText(division.inviteCode)
      setFeedback({ tone: 'success', message: `赛区“${division.name}”的邀请码已复制。` })
    } catch {
      setFeedback({ tone: 'danger', message: '浏览器拒绝访问剪贴板。' })
    }
  }

  const remove = async () => {
    if (!deleteTarget) return false
    setFeedback(null)
    try {
      await gameOperationsAdminApi.removeDivision(gameId, deleteTarget.id)
      await divisionsRequest.mutate()
      setFeedback({ tone: 'success', message: `赛区“${deleteTarget.name}”已删除。` })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '赛区删除失败。') })
      return false
    }
  }

  const columns: AdminDataColumn<Division>[] = [
    { id: 'division', header: '赛区', width: 'wide', render: (division) => <div className={styles.identity}><strong>{division.name}</strong><small>#{division.id} · {division.inviteCode ? '需要邀请码' : '无赛区邀请码'}</small></div> },
    { id: 'review', header: '报名审核', width: 'medium', render: (division) => <StatusBadge tone={hasGamePermission(division.defaultPermissions, GamePermission.RequireReview) ? 'warning' : 'success'}>{hasGamePermission(division.defaultPermissions, GamePermission.RequireReview) ? '需要审核' : '自动处理'}</StatusBadge> },
    { id: 'permissions', header: '默认权限', width: 'wide', visibility: 'desktop', render: (division) => <span className={styles.noticeText}>{divisionPermissionSummary(division.defaultPermissions)}</span> },
    { id: 'overrides', header: '题目覆盖', width: 'compact', render: (division) => <span className={styles.mono}>{division.challengeConfigs?.length ?? 0}</span> },
    { id: 'action', header: '操作', width: 'compact', align: 'right', render: (division) => <span className={styles.rowActions}>{division.inviteCode ? <button aria-label={`复制 ${division.name} 邀请码`} className={styles.iconButton} onClick={() => void copyInviteCode(division)} type="button"><Copy size={16} /></button> : null}<button aria-label={`编辑 ${division.name}`} className={styles.iconButton} onClick={() => openEditor(division)} type="button"><Pencil size={16} /></button><button aria-label={`删除 ${division.name}`} className={styles.iconButton} data-danger onClick={() => setDeleteTarget(division)} type="button"><Trash2 size={16} /></button></span> },
  ]

  const loading = divisionsRequest.isLoading || (ctf && challengesRequest.isLoading)
  const loadError = divisionsRequest.error || (ctf ? challengesRequest.error : null)

  return (
    <div className={styles.page}>
      <AdminPageHeader actions={<ActionButton icon={<Plus size={16} />} onClick={() => openEditor(null)} tone="primary" type="button">新建赛区</ActionButton>} description="维护报名策略、总榜关系、赛区邀请码和 CTF 题目访问权限。" eyebrow="GAME DIVISIONS" title="赛区管理" />
      <MetricStrip>
        <MetricItem detail="当前比赛" label="赛区总数" value={metrics.total} />
        <MetricItem detail="管理员处理" label="需要审核" tone={metrics.review ? 'warning' : 'neutral'} value={metrics.review} />
        <MetricItem detail="赛区级入口" label="配置邀请码" value={metrics.invite} />
        <MetricItem detail="CTF 特例" label="题目覆盖" value={metrics.overrides} />
      </MetricStrip>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {loadError ? <InlineFeedback tone="danger">{errorMessage(loadError, '赛区配置加载失败。')}</InlineFeedback> : null}
      {loading ? <DataState description="正在读取赛区和题目权限。" loading title="赛区加载中" /> : <DataTable caption="比赛赛区管理列表" columns={columns} emptyDescription="创建赛区后可配置报名和题目访问策略。" emptyTitle="尚未配置赛区" onRowClick={openEditor} rowKey={(division) => division.id} rows={divisions} />}
      <DivisionEditorDrawer challenges={challengesRequest.challenges ?? []} division={activeDivision} game={game} onClose={() => { setEditorOpen(false); setActiveDivision(null) }} onSaved={divisionsRequest.mutate} open={editorOpen} />
      <VNextConfirmDialog confirmationText={deleteTarget?.name} description="已报名队伍引用该赛区时，服务端可能拒绝删除。" message={deleteTarget ? `将永久删除赛区“${deleteTarget.name}”及其题目权限配置。` : ''} onClose={() => setDeleteTarget(null)} onConfirm={remove} open={Boolean(deleteTarget)} title="删除比赛赛区？" />
    </div>
  )
}
