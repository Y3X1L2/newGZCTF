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
  rollout,
  onClose,
  onRebuild,
  onCleanup,
}: {
  target: TeamLabGameTarget | null
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
              <div><dt>最后更新</dt><dd>{formatAdminDate(target.updatedAt)}</dd></div>
            </dl>
            {target.error ? <InlineFeedback tone="danger">{target.error}</InlineFeedback> : null}
            {target.runtimeId ? <Link className={styles.runtimeLink} to={`/admin/teamlab?runtime=${encodeURIComponent(target.runtimeId)}`}><ExternalLink size={16} />打开环境</Link> : null}
          </div>
        ) : null}
      </DetailDrawer>
      <VNextConfirmDialog
        confirmLabel={confirm === 'cleanup' ? '确认清理' : '确认重建'}
        description={confirm === 'cleanup' ? '删除该队伍环境及其运行数据。' : '清除当前修改，恢复为比赛选定的场景版本。'}
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
