import { AlertTriangle, CheckCircle2, CircleDashed, PlayCircle } from 'lucide-react'
import { ActionButton } from '../../../../shared/Interaction'
import { StatusBadge } from '../../shared/AdminWorkbench'
import type { TeamLabAdminReleaseReadiness } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { ReleasePlanPanel } from './ReleasePlanPanel'
import styles from './TeamLabReleasesPage.module.css'

export function ReleaseReadinessPanel({
  readiness,
  creatingTrial,
  onCreateTrial,
}: {
  readiness: TeamLabAdminReleaseReadiness
  creatingTrial: boolean
  onCreateTrial: () => void
}) {
  return (
    <div className={styles.readinessStack}>
      <section aria-labelledby="release-readiness-heading" className={styles.detailSection}>
        <header className={styles.sectionHeading}>
          <div>
            <span>RUNTIME READINESS</span>
            <h3 id="release-readiness-heading">运行就绪度</h3>
          </div>
          <StatusBadge tone={readiness.ready ? 'success' : 'warning'}>
            {readiness.ready ? '可创建试运行' : '存在阻断项'}
          </StatusBadge>
        </header>

        {readiness.blockingReasons.length ? (
          <ul className={styles.blockers}>
            {readiness.blockingReasons.map((reason) => <li key={reason}><AlertTriangle size={16} />{reason}</li>)}
          </ul>
        ) : (
          <p className={styles.readyMessage}><CheckCircle2 size={17} />调度能力、镜像分发与执行计划均已由服务端确认。</p>
        )}

        <div className={styles.imageTable} role="table" aria-label="镜像就绪状态">
          <div className={styles.imageHeader} role="row">
            <span role="columnheader">镜像模板</span>
            <span role="columnheader">可用节点</span>
            <span role="columnheader">已就绪</span>
            <span role="columnheader">分发中</span>
            <span role="columnheader">失败</span>
          </div>
          {readiness.images.map((image) => (
            <div className={styles.imageRow} key={image.imageTemplateId} role="row">
              <span role="cell"><strong>{image.name}</strong><small>{image.imageType} · #{image.imageTemplateId}</small></span>
              <span role="cell">{image.eligibleNodeCount}</span>
              <span role="cell">{image.readyNodeCount}</span>
              <span role="cell">{image.pendingNodeCount}</span>
              <span role="cell">{image.failedNodeCount}</span>
            </div>
          ))}
          {!readiness.images.length ? <p className={styles.emptyImages}>该版本没有需要分发的运行镜像。</p> : null}
        </div>

        <footer className={styles.readinessActions}>
          {readiness.latestTrialRuntime ? (
            <span className={styles.latestTrial}>
              <CircleDashed size={15} />最近试运行
              <TeamLabRuntimeStatusBadge status={readiness.latestTrialRuntime.status} />
            </span>
          ) : <span className={styles.latestTrial}>尚未创建试运行</span>}
          <ActionButton
            disabled={!readiness.ready || creatingTrial}
            icon={<PlayCircle size={16} />}
            onClick={onCreateTrial}
            tone="primary"
            type="button"
          >
            {creatingTrial ? '正在创建' : '创建试运行'}
          </ActionButton>
        </footer>
      </section>
      {readiness.plan ? <ReleasePlanPanel plan={readiness.plan} /> : null}
    </div>
  )
}
