import { Boxes, Cable, Network, Server } from 'lucide-react'
import type { TeamLabPlan } from '../api'
import styles from './TeamLabReleasesPage.module.css'

export function ReleasePlanPanel({ plan }: { plan: TeamLabPlan }) {
  return (
    <section aria-labelledby="release-plan-heading" className={styles.detailSection}>
      <header className={styles.sectionHeading}>
        <div>
          <span>PLACEMENT PLAN</span>
          <h3 id="release-plan-heading">执行计划</h3>
        </div>
        <code title={plan.planHash}>{plan.planHash.slice(0, 16)}</code>
      </header>
      <div className={styles.planMetrics}>
        <div><Network size={17} /><span>网段</span><strong>{plan.networks.length}</strong></div>
        <div><Server size={17} /><span>资产</span><strong>{plan.assets.length}</strong></div>
        <div><Boxes size={17} /><span>分片</span><strong>{plan.shards.length}</strong></div>
        <div><Cable size={17} /><span>跨分片连接</span><strong>{plan.crossShardConnections}</strong></div>
      </div>
      <dl className={styles.planFacts}>
        <div><dt>托管基础设施</dt><dd>{plan.managedInfrastructureCount}</dd></div>
        <div><dt>Bootstrap 制品</dt><dd>{plan.bootstrapArtifactCount}</dd></div>
        <div><dt>观测点估算</dt><dd>{plan.observationPointEstimate}</dd></div>
        <div><dt>所需能力</dt><dd>{plan.requiredCapabilities.length ? plan.requiredCapabilities.join(' · ') : '无额外能力'}</dd></div>
      </dl>
    </section>
  )
}
