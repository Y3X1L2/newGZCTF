import { Boxes, CheckCircle2, CircleEllipsis, DoorOpen, Trash2, TriangleAlert } from 'lucide-react'
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
        <div><span>ROLLOUT STATUS</span><h2 id="teamlab-rollout-summary">比赛环境汇总</h2></div>
        <StatusBadge pulse={meta.active} tone={meta.tone}>{meta.label}</StatusBadge>
      </header>
      <MetricStrip density="comfortable">
        <MetricItem detail="参赛队目标" label="总计" value={rollout.counts.total} />
        <MetricItem detail="等待或部署中" label="准备中" tone={waiting ? 'info' : 'neutral'} value={waiting} />
        <MetricItem detail="环境可用" label="已就绪" tone="success" value={rollout.counts.ready} />
        <MetricItem detail="选手可进入" label="入口开放" tone="success" value={rollout.counts.accessOpen} />
        <MetricItem detail="需管理员处理" label="失败" tone={rollout.counts.failed ? 'danger' : 'neutral'} value={rollout.counts.failed} />
        <MetricItem detail="结束或回收中" label="清理中" tone={rollout.counts.draining ? 'warning' : 'neutral'} value={rollout.counts.draining} />
        <MetricItem detail="资源已释放" label="已销毁" value={rollout.counts.destroyed} />
      </MetricStrip>
      <div className={styles.countLegend} aria-hidden="true">
        <span><Boxes size={14} />{rollout.counts.total}</span>
        <span><CircleEllipsis size={14} />{waiting}</span>
        <span><CheckCircle2 size={14} />{rollout.counts.ready}</span>
        <span><DoorOpen size={14} />{rollout.counts.accessOpen}</span>
        <span><TriangleAlert size={14} />{rollout.counts.failed}</span>
        <span><Trash2 size={14} />{rollout.counts.destroyed}</span>
      </div>
    </section>
  )
})
