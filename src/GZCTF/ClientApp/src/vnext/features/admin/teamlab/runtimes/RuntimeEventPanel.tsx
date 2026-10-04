import { AlertTriangle, CheckCircle2, CircleX, Info } from 'lucide-react'
import { useState } from 'react'
import { InlineFeedback, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeEvent } from '../api'
import { eventLevelLabels, runtimeStageLabel } from './runtimePresentation'
import styles from './RuntimeWorkspaces.module.css'
import type { TeamLabEventFilters } from './useRuntimeEvents'

const icons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  error: CircleX,
} as const

export function RuntimeEventPanel({
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
  const [selected, setSelected] = useState<TeamLabRuntimeEvent | null>(null)
  const ordered = [...events].sort((left, right) => right.cursor - left.cursor)
  const generations = [
    ...new Set([
      currentGeneration,
      ...events.map((event) => event.generation),
      ...(filters.generation === null ? [] : [filters.generation]),
    ]),
  ].sort((left, right) => right - left)
  const stages = [...new Set([...events.map((event) => event.stage), filters.stage])]
    .filter(Boolean)
    .sort((left, right) => left.localeCompare(right, 'zh-CN'))

  return (
    <section aria-label="运行事件">
      <div className={styles.toolbar}>
        <select
          aria-label="事件代次"
          onChange={(event) => {
            const value = event.currentTarget.value
            onFiltersChange({ ...filters, generation: value ? Number(value) : null })
          }}
          value={filters.generation ?? ''}
        >
          <option value="">全部代次</option>
          {generations.map((generation) => (
            <option key={generation} value={generation}>
              第 {generation} 代{generation === currentGeneration ? '（当前）' : ''}
            </option>
          ))}
        </select>
        <select
          aria-label="事件阶段"
          onChange={(event) => onFiltersChange({ ...filters, stage: event.currentTarget.value })}
          value={filters.stage}
        >
          <option value="">全部阶段</option>
          {stages.map((stage) => (
            <option key={stage} value={stage}>
              {runtimeStageLabel(stage)}
            </option>
          ))}
        </select>
      </div>
      {loading ? (
        <DataState loading title="正在读取运行事件" />
      ) : error ? (
        <InlineFeedback tone="danger">{errorMessage(error, '运行事件加载失败。')}</InlineFeedback>
      ) : ordered.length ? (
        <ul className={styles.eventList}>
          {ordered.map((event) => {
            const Icon = icons[event.level]
            return (
              <li data-level={event.level} key={event.cursor}>
                <button onClick={() => setSelected(event)} type="button">
                  <Icon size={18} />
                  <strong>{event.message}</strong>
                  <time>{formatAdminDate(event.createdAt)}</time>
                </button>
              </li>
            )
          })}
        </ul>
      ) : (
        <DataState title="暂无运行事件" />
      )}
      <VNextDrawer eyebrow="" open={selected !== null} onClose={() => setSelected(null)} title="事件详情">
        {selected ? (
          <>
            <p>{selected.message}</p>
            <dl className={styles.facts}>
              <div>
                <dt>时间</dt>
                <dd>{formatAdminDate(selected.createdAt)}</dd>
              </div>
              <div>
                <dt>级别</dt>
                <dd>{eventLevelLabels[selected.level]}</dd>
              </div>
              <div>
                <dt>阶段</dt>
                <dd>{runtimeStageLabel(selected.stage)}</dd>
              </div>
              <div>
                <dt>代次</dt>
                <dd>第 {selected.generation} 代</dd>
              </div>
              {selected.objectType ? (
                <div>
                  <dt>对象</dt>
                  <dd>
                    {selected.objectType} · {selected.objectId}
                  </dd>
                </div>
              ) : null}
            </dl>
          </>
        ) : null}
      </VNextDrawer>
    </section>
  )
}
