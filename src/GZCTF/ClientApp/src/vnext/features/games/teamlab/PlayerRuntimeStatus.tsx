import { Activity, CheckCircle2, CircleAlert, LoaderCircle } from 'lucide-react'
import type { TeamLabPlayerWorkspaceProjection } from './api'
import { StatusPill } from '../../../shared/Primitives'
import styles from './TeamLabWorkspacePage.module.css'

function runtimeMeta(status: TeamLabPlayerWorkspaceProjection['status']) {
  if (status === 'running') return { label: '环境已就绪', tone: 'success' as const, icon: CheckCircle2 }
  if (status === 'failed') return { label: '环境异常', tone: 'warning' as const, icon: CircleAlert }
  if (status === 'destroyed' || status === 'stopped') return { label: '环境已停止', tone: 'neutral' as const, icon: Activity }
  return { label: '环境准备中', tone: 'info' as const, icon: LoaderCircle }
}

export function PlayerRuntimeStatus({ workspace }: { workspace: TeamLabPlayerWorkspaceProjection }) {
  const meta = runtimeMeta(workspace.status)
  const Icon = meta.icon
  return (
    <section className={styles.runtimeStatus}>
      <div className={styles.runtimeIdentity}>
        <Icon aria-hidden="true" data-active={workspace.status !== 'running' || undefined} size={22} />
        <div><span>队伍环境</span><strong>{workspace.teamName}</strong></div>
      </div>
      <StatusPill tone={meta.tone}>{meta.label}</StatusPill>
      <dl>
        <div><dt>当前阶段</dt><dd>{workspace.stage}</dd></div>
        <div><dt>目标进度</dt><dd>{workspace.solvedCount} / {workspace.objectiveCount}</dd></div>
        <div><dt>环境重置</dt><dd>{workspace.resetAllowance.remaining} 次剩余</dd></div>
      </dl>
    </section>
  )
}
