import { RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeStatus } from '../api'
import { runtimeStageLabel } from './runtimePresentation'
import { useTaskHistory } from './useTaskHistory'
import styles from './RuntimeWorkspaces.module.css'

const operations: Record<string, string> = { create: '创建环境', reset: '重置环境', destroy: '销毁环境', pause: '暂停', resume: '恢复', stop: '停止', extend: '续期', assetcontrol: '资产操作', update: '更新资产' }
const statuses: Record<string, string> = { pending: '等待执行', scheduling: '正在调度', scheduled: '等待节点', running: '执行中', succeeded: '成功', failed: '失败', cancelled: '已取消' }
const stages: Record<string, string> = { ready: '已就绪', failed: '执行失败', stopping: '正在停止', 'runtime-ready': '环境已就绪' }
const failures: Record<string, string> = {
  guest_network_apply_failed: '虚拟机网络配置失败',
  guest_network_interface_missing: '虚拟机未识别需要配置的网卡',
  'operation.unclassified_failure': '操作失败，原因尚未分类',
  node_unavailable: '执行节点不可用',
}
type Task = NonNullable<ReturnType<typeof useTaskHistory>['data']>['items'][number]
const operationLabel = (value: string) => operations[value] ?? '其他环境操作'
const statusLabel = (value: string) => statuses[value] ?? '状态待确认'
const stageLabel = (value: string) => stages[value] ?? (runtimeStageLabel(value) === value ? '其他执行阶段' : runtimeStageLabel(value))
const failureLabel = (code: string) => failures[code] ?? '操作失败，详情中可查看错误码'

export function TaskHistoryPanel({ runtimeId, generation, currentStatus }: { runtimeId: string; generation: number; currentStatus: TeamLabRuntimeStatus }) {
  const history = useTaskHistory(runtimeId, generation)
  const [selected, setSelected] = useState<Task | null>(null)
  const selectedFailure = selected?.errorCode ?? selected?.blockedReasonCode
  return <section aria-label="操作记录" className={styles.recordPanel}>
    <div className={styles.toolbar}><label><input type="checkbox" checked={history.currentOnly} onChange={event => history.setCurrentOnly(event.currentTarget.checked)} /> 当前运行</label>
      <ActionButton aria-label="刷新操作记录" title="刷新操作记录" icon={<RefreshCw size={16} />} disabled={history.isValidating} onClick={() => void history.mutate()} type="button" /></div>
    <p className={styles.recordScope}>每条结果仅对应当次操作；当前环境状态以页首为准。</p>
    {history.error ? <InlineFeedback tone="danger">{errorMessage(history.error, '操作记录读取失败。')}
        <ActionButton aria-label="重新读取操作记录" icon={<RefreshCw size={15} />} onClick={() => void history.mutate()} type="button" />
      </InlineFeedback>
      : history.isLoading ? <DataState loading title="正在读取操作记录" /> : history.data?.items.length ? <ol className={styles.timeline}>
        {history.data.items.map(task => <li key={task.id}><button aria-haspopup="dialog" data-status={task.status} onClick={() => setSelected(task)} type="button">
          <span className={styles.recordMain}><strong>{operationLabel(task.operation)}</strong>
            <small>第 {task.generation} 代 · {stageLabel(task.stage)}</small>
            {task.errorCode || task.blockedReasonCode ? <span className={styles.failure}>{failureLabel(task.errorCode ?? task.blockedReasonCode!)}</span> : null}
            {task.status === 'failed' && currentStatus === 'running' ? <small>这次操作失败，当前环境仍在运行</small> : null}
          </span>
          <span className={styles.recordSide}><StatusBadge tone={task.status === 'failed' ? 'danger' : task.status === 'succeeded' ? 'success' : task.status === 'cancelled' ? 'neutral' : 'info'}>{statusLabel(task.status)}</StatusBadge>
            <time dateTime={new Date(task.createdAt).toISOString()}>{formatAdminDate(task.createdAt)}</time></span>
        </button></li>)}
      </ol> : <DataState title="暂无操作记录" />}
    <CursorPaginationBar hasNext={!history.error && !!history.data?.nextCursor} onNext={history.next} onPrevious={history.previous} page={history.page} label="操作记录分页" />
    <VNextDrawer eyebrow="" open={selected !== null} onClose={() => setSelected(null)} title={selected ? operationLabel(selected.operation) : '操作详情'}>
      {selected ? <dl className={styles.facts}>
        <div><dt>状态</dt><dd>{statusLabel(selected.status)}</dd></div>
        <div><dt>代次</dt><dd>第 {selected.generation} 代</dd></div>
        <div><dt>执行阶段</dt><dd>{stageLabel(selected.stage)}</dd></div>
        <div><dt>原始操作类型</dt><dd><code>{selected.operation}</code></dd></div>
        <div><dt>原始执行阶段</dt><dd><code>{selected.stage}</code></dd></div>
        <div><dt>开始时间</dt><dd>{selected.startedAt ? formatAdminDate(selected.startedAt) : '尚未开始'}</dd></div>
        <div><dt>完成时间</dt><dd>{selected.completedAt ? formatAdminDate(selected.completedAt) : '尚未完成'}</dd></div>
        {selectedFailure ? <><div><dt>失败类别</dt><dd>{failureLabel(selectedFailure)}</dd></div>
          <div><dt>原始错误码</dt><dd><code>{selectedFailure}</code></dd></div></> : null}
        {selected.operationId ? <div><dt>关联操作</dt><dd>{selected.operationId}</dd></div> : null}
        <div><dt>任务编号</dt><dd>{selected.id}</dd></div>
      </dl> : null}
    </VNextDrawer>
  </section>
}
