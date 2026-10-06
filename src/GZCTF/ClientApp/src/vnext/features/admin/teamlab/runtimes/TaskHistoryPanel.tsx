import { RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { useTaskHistory } from './useTaskHistory'
import styles from './RuntimeWorkspaces.module.css'

const operations: Record<string, string> = { create: '创建环境', reset: '重置环境', destroy: '销毁环境', pause: '暂停', resume: '恢复', stop: '停止', extend: '续期', assetcontrol: '资产操作', update: '更新资产' }
const statuses: Record<string, string> = { pending: '等待执行', scheduling: '正在调度', scheduled: '等待节点', running: '执行中', succeeded: '成功', failed: '失败', cancelled: '已取消' }
type Task = NonNullable<ReturnType<typeof useTaskHistory>['data']>['items'][number]

export function TaskHistoryPanel({ runtimeId, generation }: { runtimeId: string; generation: number }) {
  const history = useTaskHistory(runtimeId, generation)
  const [selected, setSelected] = useState<Task | null>(null)
  return <section aria-label="操作记录">
    <div className={styles.toolbar}><label><input type="checkbox" checked={history.currentOnly} onChange={event => history.setCurrentOnly(event.currentTarget.checked)} /> 当前运行</label>
      <ActionButton aria-label="刷新操作记录" title="刷新操作记录" icon={<RefreshCw size={16} />} disabled={history.isValidating} onClick={() => void history.mutate()} type="button" /></div>
    <p className={styles.recordScope}>当前任务记录未提供操作人和目标对象；详细执行阶段可在记录中查看。</p>
    {history.error ? <InlineFeedback tone="danger">{errorMessage(history.error, '操作记录读取失败。')}
        <ActionButton aria-label="重新读取操作记录" icon={<RefreshCw size={15} />} onClick={() => void history.mutate()} type="button" />
      </InlineFeedback>
      : history.isLoading ? <DataState loading title="正在读取操作记录" /> : history.data?.items.length ? <ol className={styles.timeline}>
        {history.data.items.map(task => <li key={task.id}><button onClick={() => setSelected(task)} type="button">
          <span><strong>{operations[task.operation] ?? task.operation}</strong><small>{task.stage}</small>
            {task.errorCode || task.blockedReasonCode ? <small className={styles.failure}>{task.errorCode ?? task.blockedReasonCode}</small> : null}</span>
          <StatusBadge tone={task.status === 'failed' ? 'danger' : task.status === 'succeeded' ? 'success' : task.status === 'cancelled' ? 'neutral' : 'info'}>{statuses[task.status] ?? task.status}</StatusBadge>
          <time dateTime={new Date(task.createdAt).toISOString()}>{formatAdminDate(task.createdAt)}</time>
        </button></li>)}
      </ol> : <DataState title="暂无操作记录" />}
    <CursorPaginationBar hasNext={!history.error && !!history.data?.nextCursor} onNext={history.next} onPrevious={history.previous} page={history.page} label="操作记录分页" />
    <VNextDrawer eyebrow="" open={selected !== null} onClose={() => setSelected(null)} title={selected ? operations[selected.operation] ?? selected.operation : '操作详情'}>
      {selected ? <dl className={styles.facts}>
        <div><dt>状态</dt><dd>{statuses[selected.status] ?? selected.status}</dd></div><div><dt>执行阶段</dt><dd>{selected.stage}</dd></div>
        <div><dt>开始时间</dt><dd>{selected.startedAt ? formatAdminDate(selected.startedAt) : '尚未开始'}</dd></div>
        <div><dt>完成时间</dt><dd>{selected.completedAt ? formatAdminDate(selected.completedAt) : '尚未完成'}</dd></div>
        {selected.errorCode || selected.blockedReasonCode ? <div><dt>失败信息</dt><dd>{selected.errorCode ?? selected.blockedReasonCode}</dd></div> : null}
        <div><dt>任务编号</dt><dd>{selected.id}</dd></div>
      </dl> : null}
    </VNextDrawer>
  </section>
}
