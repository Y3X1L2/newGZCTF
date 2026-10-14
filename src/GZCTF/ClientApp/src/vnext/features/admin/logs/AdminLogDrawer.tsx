import { useTranslation } from 'react-i18next'
import type { AdminLogEntry } from '../api'
import { DetailDrawer, StatusBadge } from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import styles from './AdminLogsPage.module.css'
import {
  adminLogActor,
  adminLogErrorCategory,
  adminLogHasDiagnostics,
  adminLogIp,
  adminLogNode,
  adminLogObject,
  adminLogResult,
  adminLogSeverity,
  adminLogTitle,
} from './adminLogPresentation'

function Fact({ label, value, wide = false }: { label: string; value: React.ReactNode; wide?: boolean }) {
  if (value === null || value === undefined || value === '') return null
  return (
    <div className={wide ? styles.detailWide : undefined}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

export function AdminLogDrawer({ log, onClose }: { log: AdminLogEntry | null; onClose: () => void }) {
  const { t } = useTranslation('admin')
  const text = (key: string) => t(`logs.${key}`)
  const result = log ? adminLogResult(log, text) : null
  const level = log ? adminLogSeverity(log, text) : null
  const diagnostic = log ? adminLogHasDiagnostics(log) : false
  return (
    <DetailDrawer onClose={onClose} open={Boolean(log)} title={log ? adminLogTitle(log, text) : text('detail_title')}>
      {log ? (
        <div className={styles.detailBody}>
          <section className={styles.detailIdentity}>
            <div>
              <span>{text('object_label')}</span>
              <strong>{adminLogObject(log, text)}</strong>
            </div>
            <StatusBadge tone={result?.tone}>{result?.label}</StatusBadge>
          </section>
          <dl className={styles.detailGrid}>
            <Fact label={text('time')} value={formatAdminDate(log.time)} />
            <Fact label={text('actor_label')} value={adminLogActor(log, text)} />
            {log.resourceType === 'worker-node' && log.workerNodeId && log.resourceId === log.workerNodeId ? null : (
              <Fact label={text('node')} value={adminLogNode(log, text)} wide />
            )}
            <Fact label={text('ip')} value={adminLogIp(log, text)} wide />
          </dl>
          {diagnostic ? (
            <section className={styles.diagnosticBlock}>
              <h3>{text('diagnostics')}</h3>
              <dl className={styles.detailGrid}>
                <Fact
                  label={text('level_label')}
                  value={<StatusBadge tone={level?.tone}>{level?.label}</StatusBadge>}
                />
                <Fact label={text('error_category')} value={adminLogErrorCategory(log, text)} />
                <Fact label={text('error_code')} value={log.errorCode} />
                <Fact
                  label={text('retryable')}
                  value={log.retryable == null ? null : text(log.retryable ? 'yes' : 'no')}
                />
              </dl>
              {log.msg ? (
                <div className={styles.messageBlock}>
                  <span>{text('message')}</span>
                  <pre>{log.msg}</pre>
                </div>
              ) : null}
              {log.detail && Object.keys(log.detail).length ? (
                <div className={styles.messageBlock}>
                  <span>{text('detail')}</span>
                  <pre>{JSON.stringify(log.detail, null, 2)}</pre>
                </div>
              ) : null}
            </section>
          ) : null}
          <section className={styles.technical}>
            <h3>{text('technical')}</h3>
            <dl className={styles.detailGrid}>
              <Fact label={text('log_id')} value={log.id && log.id > 0 ? log.id : null} wide={diagnostic} />
              {!diagnostic ? <Fact label={text('level_label')} value={level?.label} /> : null}
              <Fact label={text('event_code')} value={log.eventCode} wide />
              <Fact label={text('source')} value={log.logger} wide />
              <Fact label={text('actor_id')} value={log.actorUserId} wide />
              <Fact label={text('resource_type')} value={log.resourceType} wide />
              <Fact label={text('resource_id')} value={log.resourceId} wide />
              {log.resourceType === 'worker-node' && log.resourceId === log.workerNodeId ? null : (
                <Fact label={text('node_id')} value={log.workerNodeId} wide />
              )}
              <Fact label={text('correlation_id')} value={log.correlationId} wide />
              <Fact label={text('ticket_id')} value={log.deploymentTicketId} wide />
              <Fact label={text('trace_id')} value={log.traceId} wide />
            </dl>
            {!diagnostic ? (
              <div className={styles.messageBlock}>
                <span>{text('message')}</span>
                <pre>{log.msg || text('no_message')}</pre>
              </div>
            ) : null}
            {!diagnostic && log.detail && Object.keys(log.detail).length ? (
              <div className={styles.messageBlock}>
                <span>{text('detail')}</span>
                <pre>{JSON.stringify(log.detail, null, 2)}</pre>
              </div>
            ) : null}
          </section>
        </div>
      ) : null}
    </DetailDrawer>
  )
}
