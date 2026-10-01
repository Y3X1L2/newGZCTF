import { AlertTriangle, CheckCircle2, CircleX, Info } from 'lucide-react'
import { useState } from 'react'
import { InlineFeedback, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeEvent } from '../api'
import type { TeamLabEventFilters } from './useRuntimeEvents'
import { eventLevelLabels } from './runtimePresentation'
import styles from './RuntimeWorkspaces.module.css'

const icons = { info: Info, success: CheckCircle2, warning: AlertTriangle, error: CircleX }
export function RuntimeEventPanel({ events, error, loading, currentGeneration, filters, onFiltersChange }: {
  events: readonly TeamLabRuntimeEvent[]; error?: unknown; loading: boolean; currentGeneration: number
  filters: TeamLabEventFilters; onFiltersChange: (filters: TeamLabEventFilters) => void
}) {
  const [selected, setSelected] = useState<TeamLabRuntimeEvent | null>(null)
  const ordered = [...events].sort((a, b) => b.cursor - a.cursor)
  return <section aria-label="运行事件">
    <div className={styles.toolbar}><select aria-label="事件范围" value={filters.generation ?? ''} onChange={event => onFiltersChange({ ...filters, generation: event.currentTarget.value ? Number(event.currentTarget.value) : null })}>
      <option value="">全部记录</option><option value={currentGeneration}>当前运行</option></select>
      <select aria-label="事件阶段" value={filters.stage} onChange={event => onFiltersChange({ ...filters, stage: event.currentTarget.value })}>
        <option value="">全部阶段</option>{[...new Set(events.map(event => event.stage).concat(filters.stage))].filter(Boolean).map(stage => <option key={stage} value={stage}>{stage}</option>)}
      </select>
    </div>
    {loading ? <DataState loading title="正在读取运行事件" /> : error ? <InlineFeedback tone="danger">{errorMessage(error, '运行事件加载失败。')}</InlineFeedback>
      : ordered.length ? <ul className={styles.eventList}>{ordered.map(event => {
        const Icon = icons[event.level]
        return <li key={event.cursor} data-level={event.level}><button onClick={() => setSelected(event)} type="button"><Icon size={18} /><strong>{event.message}</strong><time>{formatAdminDate(event.createdAt)}</time></button></li>
      })}</ul> : <DataState title="暂无运行事件" />}
    <VNextDrawer eyebrow="" open={selected !== null} onClose={() => setSelected(null)} title="事件详情">
      {selected ? <><p>{selected.message}</p><dl className={styles.facts}>
        <div><dt>时间</dt><dd>{formatAdminDate(selected.createdAt)}</dd></div><div><dt>级别</dt><dd>{eventLevelLabels[selected.level]}</dd></div>
        <div><dt>阶段</dt><dd>{selected.stage}</dd></div>{selected.objectType ? <div><dt>对象</dt><dd>{selected.objectType} · {selected.objectId}</dd></div> : null}
      </dl></> : null}
    </VNextDrawer>
  </section>
}
