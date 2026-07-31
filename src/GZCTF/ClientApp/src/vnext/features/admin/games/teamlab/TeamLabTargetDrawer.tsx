import { ExternalLink, RotateCcw, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { DetailDrawer, StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabGameRollout, TeamLabGameTarget } from '../../api/teamlabGameAdminApi'
import { targetStatusMeta } from './teamLabGamePresentation'
import styles from './TeamLabGame.module.css'

export function TeamLabTargetDrawer({
  target,
  topologyId,
  rollout,
  onClose,
  onRebuild,
  onCleanup,
}: {
  target: TeamLabGameTarget | null
  topologyId: string | null
  rollout: TeamLabGameRollout | null
  onClose: () => void
  onRebuild: (target: TeamLabGameTarget) => Promise<void>
  onCleanup: (target: TeamLabGameTarget) => Promise<void>
}) {
  const [confirm, setConfirm] = useState<'rebuild' | 'cleanup' | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const meta = target ? targetStatusMeta[target.status] : null
  const rolloutClosed = rollout?.status === 'draining' || rollout?.status === 'completed'
  const transition = target?.status === 'provisioning' || target?.status === 'draining' || target?.status === 'cleanuppending'
  const canRebuild = Boolean(target && !rolloutClosed && !transition && target.status !== 'destroyed')
  const canCleanup = Boolean(target?.runtimeId && !transition && target?.status !== 'destroyed')

  const execute = async () => {
    if (!target || !confirm || busy) return false
    setBusy(true)
    setActionError(null)
    try {
      if (confirm === 'rebuild') await onRebuild(target)
      else await onCleanup(target)
      setConfirm(null)
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <DetailDrawer
        description={target ? `队伍 #${target.teamId} · ${target.externalSubject}` : undefined}
        footer={target ? <div className={styles.drawerActions}><ActionButton disabled={!canRebuild || busy} icon={<RotateCcw size={16} />} onClick={() => setConfirm('rebuild')} type="button">重建环境</ActionButton><ActionButton disabled={!canCleanup || busy} icon={<Trash2 size={16} />} onClick={() => setConfirm('cleanup')} tone="danger" type="button">清理环境</ActionButton></div> : null}
        onClose={onClose}
        open={Boolean(target)}
        title={target?.displayName ?? '队伍环境'}
      >
        {target && meta ? (
          <div className={styles.targetDetail}>
            {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '队伍环境操作失败。')}</InlineFeedback> : null}
            <dl>
              <div><dt>准备状态</dt><dd><StatusBadge pulse={meta.active} tone={meta.tone}>{meta.label}</StatusBadge></dd></div>
              <div><dt>运行时状态</dt><dd>{target.runtimeStatus ?? '—'}{target.runtimeStage ? ` · ${target.runtimeStage}` : ''}</dd></div>
              <div><dt>运行时标识</dt><dd><code>{target.runtimeId ?? '尚未创建'}</code></dd></div>
              <div><dt>操作标识</dt><dd><code>{target.operationId ?? '—'}</code></dd></div>
              <div><dt>最后更新</dt><dd>{formatAdminDate(target.updatedAt)}</dd></div>
            </dl>
            {target.error ? <InlineFeedback tone="danger">{target.error}</InlineFeedback> : null}
            {target.runtimeId && topologyId ? <Link className={styles.runtimeLink} to={`/admin/teamlab/${topologyId}/runtimes/${target.runtimeId}`}><ExternalLink size={16} />打开运行时详情</Link> : null}
          </div>
        ) : null}
      </DetailDrawer>
      <VNextConfirmDialog
        confirmLabel={confirm === 'cleanup' ? '确认清理' : '确认重建'}
        description={confirm === 'cleanup' ? '该队伍当前运行环境会进入受控销毁流程。' : '当前代会被清理，并按比赛已选 Release 重建。'}
        message={target ? `目标队伍：${target.displayName}` : ''}
        onClose={() => setConfirm(null)}
        onConfirm={execute}
        open={Boolean(confirm)}
        title={confirm === 'cleanup' ? '清理队伍环境' : '重建队伍环境'}
        tone={confirm === 'cleanup' ? 'danger' : 'primary'}
      />
    </>
  )
}
