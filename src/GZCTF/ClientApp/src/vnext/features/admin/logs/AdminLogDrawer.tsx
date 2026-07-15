import type { AdminLogEntry } from '../api'
import { formatAdminDate } from '../shared/adminFormat'
import { DetailDrawer, StatusBadge } from '../shared/AdminWorkbench'
import {
  adminLogLevelMeta,
  adminLogResource,
  adminLogSource,
  adminLogStatusMeta,
} from './adminLogPresentation'
import styles from './AdminLogsPage.module.css'

function Fact({ label, value, wide = false }: { label: string; value: React.ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? styles.detailWide : undefined}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

export function AdminLogDrawer({ log, onClose }: { log: AdminLogEntry | null; onClose: () => void }) {
  const level = log ? adminLogLevelMeta(log.level) : null
  const status = log ? adminLogStatusMeta(log.status) : null

  return (
    <DetailDrawer
      description={log ? `${adminLogSource(log)} · ${formatAdminDate(log.time)}` : undefined}
      onClose={onClose}
      open={Boolean(log)}
      title={log?.eventCode || log?.resourceDisplayName || '系统日志详情'}
    >
      {log ? (
        <div className={styles.detailBody}>
          <section className={styles.detailIdentity}>
            <div>
              <span>{log.id === undefined ? '实时日志' : `日志 #${log.id}`}</span>
              <strong>{adminLogResource(log)}</strong>
            </div>
            <div className={styles.badgeGroup}>
              <StatusBadge tone={level?.tone}>{level?.label}</StatusBadge>
              {log.status ? <StatusBadge tone={status?.tone}>{status?.label}</StatusBadge> : null}
            </div>
          </section>

          <section className={styles.messageBlock}>
            <span>消息正文</span>
            <pre>{log.msg || '无消息正文。'}</pre>
          </section>

          <dl className={styles.detailGrid}>
            <Fact label="时间" value={formatAdminDate(log.time)} />
            <Fact label="级别" value={log.level || '—'} />
            <Fact label="用户" value={log.name || '—'} />
            <Fact label="来源 IP" value={log.ip || '—'} />
            <Fact label="节点" value={log.workerNodeName || log.workerNodeId || '—'} wide />
            <Fact label="事件代码" value={log.eventCode || '—'} />
            <Fact label="任务状态" value={log.status || '—'} />
            <Fact label="错误分类" value={log.errorCategory || '—'} />
            <Fact label="错误代码" value={log.errorCode || '—'} />
            <Fact label="资源类型" value={log.resourceType || '—'} />
            <Fact label="资源" value={adminLogResource(log)} />
            <Fact label="关联 ID" value={log.correlationId || '—'} wide />
            <Fact label="部署任务 ID" value={log.deploymentTicketId || '—'} wide />
            <Fact label="Trace ID" value={log.traceId || '—'} wide />
          </dl>
        </div>
      ) : null}
    </DetailDrawer>
  )
}
