import { AlertTriangle, ArrowRight, LoaderCircle } from 'lucide-react'
import type { TeamLabRuntime } from '../api'
import styles from './TeamLabRuntimeDetailPage.module.css'

const statuses: Record<string, string> = { pending: '等待执行', scheduling: '正在调度', scheduled: '等待节点', running: '正在执行' }
export function RuntimeTaskPanel({ runtime, onInspect }: { runtime: TeamLabRuntime; onInspect: () => void }) {
  const active = runtime.queueStatus && statuses[runtime.queueStatus]
  const failure = runtime.failure?.detail || (runtime.status === 'failed' || runtime.status === 'cleanup-pending' ? runtime.error : null)
  if (!active && !failure) return null
  return <div className={styles.taskBanner} data-failed={!!failure || undefined} role="status">
    {failure ? <AlertTriangle size={20} /> : <LoaderCircle size={20} className={styles.spinner} />}
    <div><strong>{failure ? '操作未完成' : active}</strong><span>{failure || runtime.subStages?.find(stage => stage.status === 'running')?.message}</span></div>
    <button onClick={onInspect} type="button">查看记录<ArrowRight size={16} /></button>
  </div>
}
