import { FileSearch } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import type { TeamLabRuntime } from '../api'
import styles from './RuntimePanels.module.css'

const statuses: Record<string, string> = {
  pending: '等待执行', scheduling: '正在调度', scheduled: '等待节点执行', running: '执行中',
  succeeded: '已完成', failed: '执行失败', cancelled: '已取消',
}

export function RuntimeTaskPanel({ runtime, onInspect }: {
  runtime: Pick<TeamLabRuntime, 'deploymentQueueTicketId' | 'queueStatus' | 'currentOperationId' | 'subStages' | 'failure'>
  onInspect: () => void
}) {
  if (!runtime.deploymentQueueTicketId && !runtime.failure) return null
  return <section className={styles.panel} aria-label="运行任务">
    <header className={styles.panelHeader}>
      <h3>运行任务</h3>
      <ActionButton icon={<FileSearch size={16} />} onClick={onInspect} type="button">查看运行事件</ActionButton>
    </header>
    <dl className={styles.diagnosticsFacts}>
      <div><dt>任务状态</dt><dd>{runtime.queueStatus ? statuses[runtime.queueStatus] : '无队列任务'}</dd></div>
      <div><dt>部署票据</dt><dd>{runtime.deploymentQueueTicketId ?? '-'}</dd></div>
      {runtime.currentOperationId ? <div><dt>外部操作</dt><dd>{runtime.currentOperationId}</dd></div> : null}
      {(runtime.subStages ?? []).map(stage => <div key={stage.id}>
        <dt>{stage.id}</dt><dd>{stage.message || statuses[stage.status] || stage.status}</dd>
      </div>)}
    </dl>
    {runtime.failure ? <InlineFeedback tone="danger">
      <span>{runtime.failure.detail || '运行操作未能完成。'}</span>{' '}
      <span>错误码：{runtime.failure.code}；阶段：{runtime.failure.stage}。</span>{' '}
      <span>{runtime.failure.retryable ? '允许重试，请先处理错误原因。' : '不能直接重试，请先检查运行事件。'}</span>
    </InlineFeedback> : null}
  </section>
}
