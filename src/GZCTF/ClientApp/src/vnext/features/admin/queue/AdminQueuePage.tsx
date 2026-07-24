import { Eye, RefreshCw, Search } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { deploymentQueueAdminApi, type DeploymentTask } from '../api'
import {
  AdminPageHeader,
  CursorPaginationBar,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { useAdminCursorState } from '../shared/useAdminCursorState'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminQueuePage.module.css'
import { DeploymentTaskDrawer } from './DeploymentTaskDrawer'
import {
  activeDeploymentStatuses,
  deploymentSlotsLabel,
  deploymentStageLabel,
  deploymentStatusMeta,
  formatDeploymentDuration,
} from './deploymentQueuePresentation'
import { useDeploymentQueue, useDeploymentTask } from './useDeploymentQueue'

const PAGE_SIZE = 20

export function AdminQueuePage() {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const statusFilter = queryState.params.get('status') ?? ''
  const cursorState = useAdminCursorState(statusFilter)
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<DeploymentTask | null>(null)
  const [cancelPendingIds, setCancelPendingIds] = useState<Set<string>>(() => new Set())
  const [failure, setFailure] = useState<string | null>(null)
  const queueRequest = useDeploymentQueue({
    status: statusFilter || undefined,
    page: queryState.page,
    pageSize: PAGE_SIZE,
    cursor: cursorState.cursor,
  })
  const detailRequest = useDeploymentTask(selectedId)
  const tasks = queueRequest.queue?.items ?? []

  useEffect(() => {
    if (!queueRequest.queue) return
    setCancelPendingIds((current) => {
      const next = new Set(
        [...current].filter((id) => {
          const task = queueRequest.queue?.items.find((item) => item.id === id)
          return !task || activeDeploymentStatuses.has(task.statusKey.toLowerCase())
        })
      )
      if (next.size === current.size && [...next].every((id) => current.has(id))) return current
      return next
    })
  }, [queueRequest.queue])

  const visible = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    if (!normalized) return tasks
    return tasks.filter((task) =>
      [task.requestLabel, task.ownerLabel, task.gameLabel, task.challengeLabel, task.image, task.targetNodeLabel]
        .filter(Boolean)
        .some((value) => value?.toLowerCase().includes(normalized))
    )
  }, [query, tasks])

  const metrics = useMemo(
    () => ({
      total: tasks.length,
      active: tasks.filter((task) => activeDeploymentStatuses.has(task.statusKey.toLowerCase())).length,
      failed: tasks.filter((task) => task.statusKey.toLowerCase() === 'failed').length,
      dockerSlots: tasks.reduce((total, task) => total + task.dockerSlots, 0),
      vmSlots: tasks.reduce((total, task) => total + task.vmSlots, 0),
    }),
    [tasks]
  )

  const columns: AdminDataColumn<DeploymentTask>[] = [
    {
      id: 'request',
      header: '请求',
      width: 'wide',
      render: (task) => (
        <div className={styles.taskName}>
          <strong>{task.requestLabel}</strong>
          <small>{task.ownerLabel || `任务 ${task.id.slice(0, 8)}`}</small>
        </div>
      ),
    },
    {
      id: 'kind',
      header: '类型 / 操作',
      width: 'medium',
      visibility: 'desktop',
      render: (task) => (
        <span className={styles.compactText}>
          {task.typeLabel} · {task.actionLabel}
        </span>
      ),
    },
    {
      id: 'node',
      header: '目标节点',
      width: 'wide',
      visibility: 'desktop',
      render: (task) => <span className={styles.ellipsis}>{task.targetNodeLabel || '尚未分配'}</span>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (task) => {
        const meta = deploymentStatusMeta(task.statusKey)
        const cancelling = cancelPendingIds.has(task.id) && meta.active
        return (
          <StatusBadge pulse={meta.active} tone={cancelling ? 'warning' : meta.tone}>
            {cancelling ? '取消确认中' : meta.label}
          </StatusBadge>
        )
      },
    },
    {
      id: 'stage',
      header: '阶段',
      width: 'wide',
      visibility: 'wide',
      render: (task) => <span className={styles.ellipsis}>{deploymentStageLabel(task.stage, task.stageMessage)}</span>,
    },
    {
      id: 'slots',
      header: '槽位',
      width: 'medium',
      visibility: 'wide',
      render: (task) => <span className={styles.mono}>{deploymentSlotsLabel(task)}</span>,
    },
    {
      id: 'duration',
      header: '耗时',
      width: 'medium',
      visibility: 'desktop',
      render: (task) => <span className={styles.mono}>{formatDeploymentDuration(task)}</span>,
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (task) => (
        <button
          aria-label={`查看 ${task.requestLabel}`}
          className={styles.iconButton}
          onClick={() => setSelectedId(task.id)}
          type="button"
        >
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const cancel = async () => {
    if (!cancelTarget) return false
    setFailure(null)
    setCancelPendingIds((current) => new Set(current).add(cancelTarget.id))
    try {
      await deploymentQueueAdminApi.cancel(cancelTarget.id)
      await queueRequest.mutate()
      return true
    } catch (error) {
      setCancelPendingIds((current) => {
        const next = new Set(current)
        next.delete(cancelTarget.id)
        return next
      })
      setFailure(error instanceof Error ? error.message : '部署任务取消失败。')
      return false
    }
  }

  const selected = tasks.find((task) => task.id === selectedId) ?? null
  const numberPageCount = Math.max(1, Math.ceil((queueRequest.queue?.total ?? 0) / PAGE_SIZE))
  const numberPage = Math.min(queryState.page, numberPageCount)

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <ActionButton icon={<RefreshCw size={16} />} onClick={() => void queueRequest.mutate()} type="button">
            刷新状态
          </ActionButton>
        }
        description="跟踪容器、虚拟机和场景资源从排队、调度到执行终态的全过程。"
        eyebrow="RUNTIME ORCHESTRATION"
        title="部署队列"
      />

      <MetricStrip>
        <MetricItem detail="当前加载范围" label="任务" value={metrics.total} />
        <MetricItem detail="等待或执行中" label="活跃" tone={metrics.active ? 'info' : 'neutral'} value={metrics.active} />
        <MetricItem detail="当前页需要处理" label="失败" tone={metrics.failed ? 'danger' : 'neutral'} value={metrics.failed} />
        <MetricItem detail="当前页任务占用" label="Docker 槽位" value={metrics.dockerSlots} />
        <MetricItem detail="当前页任务占用" label="VM 槽位" value={metrics.vmSlots} />
      </MetricStrip>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索当前页部署任务"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder="搜索当前页请求、节点或镜像"
              type="search"
              value={query}
            />
          </label>
          <select
            aria-label="部署状态"
            onChange={(event) => {
              cursorState.reset()
              queryState.update({ status: event.currentTarget.value || null })
            }}
            value={statusFilter}
          >
            <option value="">全部状态</option>
            <option value="pending">等待中</option>
            <option value="scheduling">调度中</option>
            <option value="scheduled">已分配</option>
            <option value="running">执行中</option>
            <option value="completed">已完成</option>
            <option value="failed">失败</option>
            <option value="cancelled">已取消</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator
          active={queueRequest.isRefreshing}
          label={metrics.active ? '3 秒自动同步' : '15 秒状态校验'}
        />
      </FilterToolbar>

      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}

      {queueRequest.isLoading ? (
        <DataState description="正在读取部署请求和节点调度状态。" loading title="部署队列加载中" />
      ) : queueRequest.error ? (
        <DataState description="部署队列接口暂时不可用，请检查服务端契约或连接。" title="部署队列加载失败" />
      ) : (
        <>
          <DataTable
            caption="部署任务列表"
            columns={columns}
            emptyDescription="调整状态筛选或当前页关键词后重试。"
            emptyTitle="没有符合条件的部署任务"
            onRowClick={(task) => setSelectedId(task.id)}
            rowKey={(task) => task.id}
            rows={visible}
          />
          {queueRequest.queue?.contract === 'deployment-targets' ? (
            <PaginationBar
              onPageChange={queryState.setPage}
              page={numberPage}
              pageCount={numberPageCount}
              total={queueRequest.queue.total ?? 0}
            />
          ) : queueRequest.queue ? (
            <CursorPaginationBar
              hasNext={Boolean(queueRequest.queue.nextCursor)}
              onNext={() => queueRequest.queue?.nextCursor && cursorState.next(queueRequest.queue.nextCursor)}
              onPrevious={cursorState.previous}
              page={cursorState.page}
            />
          ) : null}
        </>
      )}

      <DeploymentTaskDrawer
        cancelPending={selected ? cancelPendingIds.has(selected.id) : false}
        detail={detailRequest.task}
        detailError={detailRequest.error}
        detailLoading={detailRequest.isLoading}
        onCancel={() => selected && setCancelTarget(selected)}
        onClose={() => setSelectedId(null)}
        task={selected}
      />

      <VNextConfirmDialog
        confirmLabel="取消任务"
        description="取消请求将由调度服务确认"
        message={`任务会保留在队列中，直到服务端返回已取消或其他终态。目标节点：${cancelTarget?.targetNodeLabel || '尚未分配'}。`}
        onClose={() => setCancelTarget(null)}
        onConfirm={cancel}
        open={Boolean(cancelTarget)}
        title={`取消 ${cancelTarget?.requestLabel ?? '部署任务'}？`}
      />
    </div>
  )
}
