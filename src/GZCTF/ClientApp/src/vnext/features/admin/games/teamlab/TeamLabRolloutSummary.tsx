import { memo } from 'react'
import { MetricItem, MetricStrip, StatusBadge } from '../../shared/AdminWorkbench'
import type { TeamLabGameRollout } from '../../api/teamlabGameAdminApi'
import { rolloutStatusMeta } from './teamLabGamePresentation'
import styles from './TeamLabGame.module.css'

export const TeamLabRolloutSummary = memo(function TeamLabRolloutSummary({ rollout }: { rollout: TeamLabGameRollout }) {
  const meta = rolloutStatusMeta[rollout.status]
  const waiting = rollout.counts.pending + rollout.counts.provisioning
  return (
    <section className={styles.summary} aria-labelledby="teamlab-rollout-summary">
      <header className={styles.sectionHeader}>
        <div><h2 id="teamlab-rollout-summary">比赛环境汇总</h2></div>
        <StatusBadge pulse={meta.active} tone={meta.tone}>{meta.label}</StatusBadge>
      </header>
      <MetricStrip density="comfortable">
        <MetricItem label="总计" value={rollout.counts.total} />
        <MetricItem label="准备中" tone={waiting ? 'info' : 'neutral'} value={waiting} />
        <MetricItem label="已就绪" tone="success" value={rollout.counts.ready} />
        <MetricItem label="入口开放" tone="success" value={rollout.counts.accessOpen} />
        <MetricItem label="失败" tone={rollout.counts.failed ? 'danger' : 'neutral'} value={rollout.counts.failed} />
        <MetricItem label="清理中" tone={rollout.counts.draining ? 'warning' : 'neutral'} value={rollout.counts.draining} />
        <MetricItem label="已销毁" value={rollout.counts.destroyed} />
      </MetricStrip>

    </section>
  )
})
