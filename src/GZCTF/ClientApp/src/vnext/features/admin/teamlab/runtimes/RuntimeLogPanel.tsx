import { memo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { AdminLogEntry } from '../../api'
import styles from './RuntimePanels.module.css'

export const RuntimeLogPanel = memo(function RuntimeLogPanel({
  logs,
  error,
  loading,
}: {
  logs: readonly AdminLogEntry[]
  error?: unknown
  loading: boolean
}) {
  const ordered = [...logs].sort((left, right) => right.time - left.time)
  return (
    <section className={styles.panel} aria-labelledby="runtime-logs-title">
      <header className={styles.panelHeader}>
        <div><span>STRUCTURED LOG</span><h3 id="runtime-logs-title">对象日志</h3></div>
        <span className={styles.muted}>持久化事件投影</span>
      </header>
      {loading ? <DataState description="正在读取结构化日志。" loading title="日志加载中" /> : error ? (
        <InlineFeedback tone="danger">{errorMessage(error, '结构化日志加载失败。')}</InlineFeedback>
      ) : ordered.length ? (
        <div className={styles.logFrame} role="log" aria-label="TeamLab 运行日志">
          {ordered.map((entry, index) => (
            <div className={styles.logRow} data-level={entry.level?.toLowerCase()} key={entry.id ?? `${entry.time}:${index}`}>
              <time>{formatAdminDate(entry.time)}</time>
              <strong>{entry.level ?? 'Info'}</strong>
              <code>{entry.eventCode ?? entry.status ?? 'runtime'}</code>
              <span>{entry.resourceDisplayName ?? entry.resourceId ?? 'runtime'}</span>
              <p>{entry.msg ?? '—'}</p>
            </div>
          ))}
        </div>
      ) : <DataState description="当前运行时尚未写入结构化日志。" title="暂无日志" />}
    </section>
  )
})
