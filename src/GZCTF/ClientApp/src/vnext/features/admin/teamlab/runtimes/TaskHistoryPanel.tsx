import { ArrowLeft, ArrowRight, RefreshCw } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import { useTaskHistory } from './useTaskHistory'
import styles from './RuntimePanels.module.css'

const operations: Record<string, string> = { create: '创建', reset: '重建', destroy: '销毁', pause: '暂停', resume: '恢复', stop: '停止', extend: '续期', assetcontrol: '单资产操作' }
const statuses: Record<string, string> = { pending: '等待执行', scheduling: '正在调度', scheduled: '等待节点', running: '执行中', succeeded: '成功', failed: '失败', cancelled: '已取消' }

export function TaskHistoryPanel({ runtimeId, generation }: { runtimeId: string; generation: number }) {
  const history = useTaskHistory(runtimeId, generation)
  return <section className={styles.panel} aria-label="任务历史">
    <header className={styles.panelHeader}>
      <h3>任务历史</h3>
      <ActionButton icon={<RefreshCw size={16} />} disabled={history.isValidating} onClick={() => void history.mutate()} type="button">刷新历史</ActionButton>
    </header>
    <label><input type="checkbox" checked={history.currentOnly} onChange={event => history.setCurrentOnly(event.target.checked)} />仅当前代次</label>
    {history.error ? <InlineFeedback tone="danger">{errorMessage(history.error, '任务历史读取失败。')}</InlineFeedback>
      : history.isLoading ? <DataState loading title="正在读取任务历史" />
      : !history.data?.items.length ? <DataState title="暂无任务记录" />
      : <div className={styles.sessionTableScroll}><table className={styles.sessionTable}>
        <thead><tr><th>操作 / 代次</th><th>状态 / 阶段</th><th>提交 / 完成时间</th><th>诊断</th><th>任务标识</th></tr></thead>
        <tbody>{history.data.items.map(task => <tr key={task.id}>
          <td>{operations[task.operation] ?? task.operation}<small>第 {task.generation} 代</small></td>
          <td>{statuses[task.status] ?? task.status}<small>{task.stage}</small></td>
          <td>{formatAdminDate(task.createdAt)}<small>{task.completedAt ? formatAdminDate(task.completedAt) : '尚未完成'}</small></td>
          <td>{task.errorCode ?? task.blockedReasonCode ?? '-'}{task.status === 'failed' ? <small>{task.retryable ? '允许重试' : '不可直接重试'}</small> : null}</td>
          <td><small>{task.id}</small>{task.operationId ? <small>operation: {task.operationId}</small> : null}</td>
        </tr>)}</tbody>
      </table></div>}
    <div className={styles.captureActions}>
      <ActionButton icon={<ArrowLeft size={16} />} disabled={history.page === 1 || history.isLoading} onClick={history.previous} type="button">上一页</ActionButton>
      <span>第 {history.page} 页</span>
      <ActionButton icon={<ArrowRight size={16} />} disabled={!history.data?.nextCursor || !!history.error || history.isLoading} onClick={history.next} type="button">下一页</ActionButton>
    </div>
  </section>
}
