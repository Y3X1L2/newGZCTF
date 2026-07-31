import { AlertTriangle, Check, LoaderCircle } from 'lucide-react'
import { memo } from 'react'
import type { TeamLabRuntime } from '../api'
import { runtimeStageOrder, runtimeStatusLabels } from './runtimePresentation'
import styles from './RuntimePanels.module.css'

export const RuntimeStageTimeline = memo(function RuntimeStageTimeline({ runtime }: { runtime: TeamLabRuntime }) {
  const currentIndex = runtimeStageOrder.indexOf(runtime.status)
  const terminalCleanup = runtime.status === 'cleanup-pending' || runtime.status === 'destroying' || runtime.status === 'destroyed'

  return (
    <section className={styles.panel} aria-labelledby="runtime-stage-title">
      <header className={styles.panelHeader}>
        <div>
          <span>DEPLOYMENT STAGES</span>
          <h3 id="runtime-stage-title">部署阶段</h3>
        </div>
        <code>{runtime.stage}</code>
      </header>
      <ol className={styles.stageTimeline}>
        {runtimeStageOrder.map((status, index) => {
          const state = runtime.status === 'failed'
            ? 'idle'
            : currentIndex === index
              ? 'active'
              : currentIndex > index || runtime.status === 'running'
                ? 'done'
                : 'idle'
          return (
            <li data-state={state} key={status}>
              <span aria-hidden="true">
                {state === 'done' ? <Check size={14} /> : state === 'active' ? <LoaderCircle size={14} /> : index + 1}
              </span>
              <strong>{runtimeStatusLabels[status]}</strong>
            </li>
          )
        })}
        {runtime.status === 'failed' ? (
          <li data-state="failed">
            <span aria-hidden="true"><AlertTriangle size={14} /></span>
            <strong>失败于 {runtime.stage}</strong>
          </li>
        ) : null}
        {terminalCleanup ? (
          <li data-state={runtime.status === 'destroying' ? 'active' : 'done'}>
            <span aria-hidden="true">7</span>
            <strong>{runtimeStatusLabels[runtime.status]}</strong>
          </li>
        ) : null}
      </ol>
      {runtime.error ? <p className={styles.errorText}>{runtime.error}</p> : null}
    </section>
  )
})
