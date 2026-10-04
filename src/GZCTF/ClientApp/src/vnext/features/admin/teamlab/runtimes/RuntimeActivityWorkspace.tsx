import { useEffect, useState } from 'react'
import type { TeamLabRuntime } from '../api'
import { RuntimeEventPanel } from './RuntimeEventPanel'
import { RuntimeLogPanel } from './RuntimeLogPanel'
import { TaskHistoryPanel } from './TaskHistoryPanel'
import { useRuntimeEvents, type TeamLabEventFilters } from './useRuntimeEvents'
import styles from './RuntimeWorkspaces.module.css'

export function RuntimeActivityWorkspace({ runtime, eventFilters, onFiltersChange }: {
  runtime: TeamLabRuntime; eventFilters: TeamLabEventFilters; onFiltersChange: (filters: TeamLabEventFilters) => void
}) {
  const [view, setView] = useState('operations')
  useEffect(() => { if (eventFilters.generation !== null || eventFilters.stage) setView('events') }, [eventFilters])
  const events = useRuntimeEvents(view === 'events' ? runtime.id : '', runtime.status, eventFilters)
  return <div>
    <nav className={styles.viewTabs} aria-label="活动记录类型">
      {[['operations', '操作记录'], ['events', '运行事件'], ['logs', '诊断日志']].map(([key, label]) =>
        <button key={key} aria-pressed={view === key} onClick={() => setView(key)} type="button">{label}</button>)}
    </nav>
    {view === 'operations' ? <TaskHistoryPanel runtimeId={runtime.id} generation={runtime.generation} /> : null}
    {view === 'events' ? <RuntimeEventPanel events={events.events} error={events.error} loading={events.isLoading}
      currentGeneration={runtime.generation} filters={eventFilters} onFiltersChange={onFiltersChange} /> : null}
    {view === 'logs' ? <RuntimeLogPanel runtimeId={runtime.id} status={runtime.status} /> : null}
  </div>
}
