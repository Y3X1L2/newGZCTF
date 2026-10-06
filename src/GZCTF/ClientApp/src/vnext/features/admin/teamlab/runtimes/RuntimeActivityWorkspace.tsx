import { useEffect, useState } from 'react'
import type { TeamLabRuntime } from '../api'
import { CapturePanel } from './CapturePanel'
import { RemoteSessionsPanel } from './RemoteSessionsPanel'
import { RuntimeEventPanel } from './RuntimeEventPanel'
import { RuntimeLogPanel } from './RuntimeLogPanel'
import { TaskHistoryPanel } from './TaskHistoryPanel'
import { useRuntimeEvents, type TeamLabEventFilters } from './useRuntimeEvents'
import styles from './RuntimeWorkspaces.module.css'

export function RuntimeActivityWorkspace({ runtime, eventFilters, onFiltersChange, initialDetail }: {
  runtime: TeamLabRuntime; eventFilters: TeamLabEventFilters; onFiltersChange: (filters: TeamLabEventFilters) => void
  initialDetail?: 'events' | 'logs' | 'capture'
}) {
  const [advanced, setAdvanced] = useState(Boolean(initialDetail))
  const [view, setView] = useState<'events' | 'logs' | 'sessions' | 'capture'>(initialDetail ?? 'events')
  useEffect(() => { if (initialDetail) { setAdvanced(true); setView(initialDetail) } }, [initialDetail])
  useEffect(() => { if (eventFilters.generation !== null || eventFilters.stage) { setAdvanced(true); setView('events') } }, [eventFilters])
  const events = useRuntimeEvents(advanced && view === 'events' ? runtime.id : '', runtime.status, eventFilters)
  return <div className={styles.activityWorkspace}>
    <TaskHistoryPanel runtimeId={runtime.id} generation={runtime.generation} currentStatus={runtime.status} />
    <details className={styles.advanced} open={advanced} onToggle={event => setAdvanced(event.currentTarget.open)}>
      <summary>高级排障与会话审计</summary>
      <nav className={styles.viewTabs} aria-label="高级排障类型">
        {([['events', '运行事件'], ['logs', '诊断日志'], ['sessions', '远程会话'], ['capture', '限时抓包']] as const).map(([key, label]) =>
          <button key={key} aria-pressed={view === key} onClick={() => setView(key)} type="button">{label}</button>)}
      </nav>
      {view === 'events' ? <RuntimeEventPanel events={events.events} error={events.error} loading={events.isLoading}
      currentGeneration={runtime.generation} filters={eventFilters} onFiltersChange={onFiltersChange} /> : null}
      {view === 'logs' ? <RuntimeLogPanel runtimeId={runtime.id} status={runtime.status} /> : null}
      {view === 'sessions' ? <RemoteSessionsPanel runtimeId={runtime.id} /> : null}
      {view === 'capture' ? <CapturePanel runtimeId={runtime.id} networks={runtime.networks} /> : null}
    </details>
  </div>
}
