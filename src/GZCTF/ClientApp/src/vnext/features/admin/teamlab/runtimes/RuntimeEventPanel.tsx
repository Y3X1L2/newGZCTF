import { AlertTriangle, CheckCircle2, Circle, Info } from 'lucide-react'
import { memo, useMemo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { FilterToolbar, ToolbarGroup } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeEvent } from '../api'
import type { TeamLabEventFilters } from './useRuntimeEvents'
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
  currentGeneration,
  filters,
  onFiltersChange,
}: {
  events: readonly TeamLabRuntimeEvent[]
  error?: unknown
  loading: boolean
  currentGeneration: number
  filters: TeamLabEventFilters
  onFiltersChange: (filters: TeamLabEventFilters) => void
}) {
  const ordered = [...events].sort((left, right) => right.cursor - left.cursor).slice(0, 30)
  const generations = useMemo(() => {
    const seen = new Set(events.map((event) => event.generation))
    if (filters.generation !== null) seen.add(filters.generation)
    seen.add(currentGeneration)
    return [...seen].sort((left, right) => right - left)
  }, [currentGeneration, events, filters.generation])
  const stages = useMemo(() => {
    const seen = new Set(events.map((event) => event.stage).filter((stage) => stage.length > 0))
    if (filters.stage) seen.add(filters.stage)
    return [...seen].sort((left, right) => left.localeCompare(right, 'zh-CN'))
  }, [events, filters.stage])

  return (
    <section className={styles.panel} aria-labelledby="runtime-events-title">
      <header className={styles.panelHeader}>
        <div><span>持久化事件</span><h3 id="runtime-events-title">运行事件</h3></div>
        <strong>{events.length} 条</strong>
      </header>
      <FilterToolbar>
        <ToolbarGroup>
          <select
            aria-label="事件代次"
            className={styles.filterSelect}
            onChange={(event) => onFiltersChange({ ...filters, generation: event.target.value ? Number(event.target.value) : null })}
            value={filters.generation ?? ''}
          >
            <option value="">全部代次</option>
            {generations.map((generation) => (
              <option key={generation} value={generation}>第 {generation} 代</option>
            ))}
          </select>
          <select
            aria-label="事件阶段"
            className={styles.filterSelect}
            onChange={(event) => onFiltersChange({ ...filters, stage: event.target.value })}
            value={filters.stage}
          >
            <option value="">全部阶段</option>
            {stages.map((stage) => (
              <option key={stage} value={stage}>{stage}</option>
            ))}
          </select>
        </ToolbarGroup>
      </FilterToolbar>
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
      ) : <DataState description="当前条件下尚未写入事件。" title="暂无运行事件" />}
    </section>
  )
})
