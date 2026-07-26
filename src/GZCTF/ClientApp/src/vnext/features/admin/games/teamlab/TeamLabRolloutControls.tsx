import { DoorClosed, DoorOpen, PackageCheck, Power } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, VNextConfirmDialog } from '../../../../shared/Interaction'
import type { TeamLabGameBinding, TeamLabGameRollout } from '../../api/teamlabGameAdminApi'
import styles from './TeamLabGame.module.css'

export function TeamLabRolloutControls({
  binding,
  rollout,
  busy,
  configurationDirty,
  onPrepare,
  onAccess,
  onDrain,
}: {
  binding: TeamLabGameBinding | null
  rollout: TeamLabGameRollout | null
  busy: boolean
  configurationDirty: boolean
  onPrepare: () => Promise<void>
  onAccess: (open: boolean) => Promise<void>
  onDrain: () => Promise<void>
}) {
  const [drainOpen, setDrainOpen] = useState(false)
  const prepared = rollout?.status === 'ready'
  const finished = rollout?.status === 'completed' || rollout?.status === 'draining'
  const preparing = rollout?.status === 'preparing' || rollout?.status === 'rollingout'
  return (
    <section className={styles.controlSection} aria-labelledby="teamlab-controls-title">
      <header className={styles.sectionHeader}><div><span>ROLLOUT CONTROL</span><h2 id="teamlab-controls-title">比赛环境控制</h2></div></header>
      <div className={styles.controlActions}>
        <ActionButton disabled={!binding?.activeReleaseId || busy || configurationDirty || finished || preparing} icon={<PackageCheck size={16} />} onClick={() => void onPrepare()} tone="primary" type="button">提前准备环境</ActionButton>
        {rollout?.desiredAccessOpen ? (
          <ActionButton disabled={busy || finished} icon={<DoorClosed size={16} />} onClick={() => void onAccess(false)} type="button">关闭选手入口</ActionButton>
        ) : (
          <ActionButton disabled={!prepared || busy || finished} icon={<DoorOpen size={16} />} onClick={() => void onAccess(true)} type="button">开放选手入口</ActionButton>
        )}
        <ActionButton disabled={!rollout || busy || rollout.status === 'completed' || rollout.status === 'draining'} icon={<Power size={16} />} onClick={() => setDrainOpen(true)} tone="danger" type="button">结束并清理</ActionButton>
      </div>
      <VNextConfirmDialog
        confirmLabel="开始清理"
        description="选手入口会立即关闭，各队运行环境随后进入受控销毁流程。"
        message="此操作用于比赛结束或场景版本切换前的完整回收。"
        onClose={() => setDrainOpen(false)}
        onConfirm={async () => { await onDrain(); return true }}
        open={drainOpen}
        title="结束 TeamLab rollout"
      />
    </section>
  )
}
