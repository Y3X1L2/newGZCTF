import { AlertTriangle, CheckCircle2, Download, PlayCircle } from 'lucide-react'
import { Link } from 'react-router'
import { ActionButton } from '../../../../shared/Interaction'
import type { TeamLabAdminReleaseReadiness } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './TeamLabReleasesPage.module.css'

export function ReleaseReadinessPanel({ readiness, creatingTrial, preparingImages, onCreateTrial, onPrepareImages }: {
  readiness: TeamLabAdminReleaseReadiness
  creatingTrial: boolean
  preparingImages: boolean
  onCreateTrial: () => void
  onPrepareImages: () => void
}) {
  const imagesToPrepare = readiness.images.filter(image => image.pendingNodeCount || image.failedNodeCount)
  return <section className={styles.readiness} aria-label="版本运行准备">
    <div className={styles.readinessStatus} data-ready={readiness.ready}>
      {readiness.ready ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}
      <strong>{readiness.ready ? '可以创建运行环境' : imagesToPrepare.length ? '需要准备镜像' : '暂不可创建'}</strong>
    </div>
    {readiness.plan ? <div className={styles.releaseScale}>
      <span>{readiness.plan.networks.length} 个网段</span>
      <span>{readiness.plan.assets.length} 个资产</span>
    </div> : null}
    {readiness.blockingReasons.length ? <ul className={styles.blockers}>
      {readiness.blockingReasons.map(reason => <li key={reason}>{reason}</li>)}
    </ul> : null}
    <div className={styles.readinessActions}>
      {imagesToPrepare.length ? <ActionButton disabled={preparingImages} icon={<Download size={16} />} onClick={onPrepareImages} type="button">
        {preparingImages ? '准备中' : '准备镜像'}
      </ActionButton> : null}
      <ActionButton disabled={!readiness.ready || creatingTrial} icon={<PlayCircle size={16} />} onClick={onCreateTrial} tone="primary" type="button">
        {creatingTrial ? '正在创建' : '创建试运行'}
      </ActionButton>
    </div>
    {readiness.latestTrialRuntime ? <Link className={styles.latestTrial} to={`/admin/teamlab/${readiness.topologyId}/runtimes/${readiness.latestTrialRuntime.id}`}>
      最近试运行 <TeamLabRuntimeStatusBadge status={readiness.latestTrialRuntime.status} />
    </Link> : null}
    {readiness.images.length ? <details className={styles.imageDetails}>
      <summary>镜像分发详情</summary>
      <table><thead><tr><th>模板</th><th>已就绪</th><th>待分发</th><th>失败</th></tr></thead><tbody>
        {readiness.images.map(image => <tr key={image.imageTemplateId}>
          <td>{image.name}</td><td>{image.readyNodeCount}/{image.eligibleNodeCount}</td>
          <td>{image.pendingNodeCount}</td><td>{image.failedNodeCount}</td>
        </tr>)}
      </tbody></table>
    </details> : null}
  </section>
}
