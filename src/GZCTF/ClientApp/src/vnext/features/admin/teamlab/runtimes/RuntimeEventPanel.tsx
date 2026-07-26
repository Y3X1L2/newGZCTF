import { AlertTriangle, CheckCircle2, Circle, Info } from 'lucide-react'
import { memo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeEvent } from '../api'
import { eventLevelLabels } from './runtimePresentation'
import styles from './RuntimePanels.module.css'

const levelIcons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  error: Circle,
} as const

export const RuntimeEventPanel = memo(function RuntimeEventPanel({
  events,
  error,
  loading,
}: {
  events: readonly TeamLabRuntimeEvent[]
  error?: unknown
  loading: boolean
}) {
  const ordered = [...events].sort((left, right) => right.cursor - left.cursor).slice(0, 30)
  return (
    <section className={styles.panel} aria-labelledby="runtime-events-title">
      <header className={styles.panelHeader}>
        <div><span>PERSISTED EVENTS</span><h3 id="runtime-events-title">运行事件</h3></div>
        <strong>{events.length} 条</strong>
      </header>
      {loading ? <DataState description="正在读取运行事件。" loading title="事件加载中" /> : error ? (
        <InlineFeedback tone="danger">{errorMessage(error, '运行事件加载失败。')}</InlineFeedback>
      ) : ordered.length ? (
        <ol className={styles.eventTimeline}>
          {ordered.map((event) => {
            const Icon = levelIcons[event.level]
            return (
              <li data-level={event.level} key={event.cursor}>
                <span aria-hidden="true"><Icon size={15} /></span>
                <div><strong>{event.message}</strong><small>{event.stage} · 第 {event.generation} 代 · {eventLevelLabels[event.level]}</small></div>
                <time>{formatAdminDate(event.createdAt)}</time>
              </li>
            )
          })}
        </ol>
      ) : <DataState description="当前运行时尚未写入事件。" title="暂无运行事件" />}
    </section>
  )
})
