import { Eye, ListPlus, RefreshCw, RotateCw, Search, Wifi, WifiOff } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import type { AdminLogEntry } from '../api'
import { formatAdminDate } from '../shared/adminFormat'
import {
  AdminPageHeader,
  CursorPaginationBar,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { useAdminCursorState } from '../shared/useAdminCursorState'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import { AdminLogDrawer } from './AdminLogDrawer'
import styles from './AdminLogsPage.module.css'
import {
  adminLogKey,
  adminLogLevelMeta,
  adminLogResource,
  adminLogSource,
  adminLogStatusMeta,
  mergeAdminLogs,
} from './adminLogPresentation'
import { useAdminLogs } from './useAdminLogs'
import { useAdminLogStream } from './useAdminLogStream'

const PAGE_SIZE = 50

function connectionMeta(state: ReturnType<typeof useAdminLogStream>['connectionState']) {
  if (state === 'connected') return { label: '实时已连接', tone: 'success' as const }
  if (state === 'reconnecting') return { label: '正在重连', tone: 'warning' as const }
  if (state === 'connecting') return { label: '正在连接', tone: 'info' as const }
  return { label: '实时已离线', tone: 'danger' as const }
}

export function AdminLogsPage() {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const level = queryState.params.get('level') ?? 'All'
  const statusFilter = queryState.params.get('status') ?? ''
  const correlationId = queryState.params.get('correlationId') || undefined
  const workerNodeId = queryState.params.get('workerNodeId') || undefined
  const deploymentTicketId = queryState.params.get('deploymentTicketId') || undefined
  const eventCode = queryState.params.get('eventCode') || undefined
  const resourceType = queryState.params.get('resourceType') || undefined
  const resourceId = queryState.params.get('resourceId') || undefined
  const scopeKey = [level, correlationId, workerNodeId, deploymentTicketId, eventCode, resourceType, resourceId].join('|')
  const cursorState = useAdminCursorState(scopeKey)
  const logsRequest = useAdminLogs({
    level,
    count: PAGE_SIZE,
    offset: (queryState.page - 1) * PAGE_SIZE,
    cursor: cursorState.cursor,
    correlationId,
    workerNodeId,
    deploymentTicketId,
    eventCode,
    resourceType,
    resourceId,
  })
  const stream = useAdminLogStream()
  const [query, setQuery] = useState('')
  const [identity, setIdentity] = useState('')
  const [merged, setMerged] = useState<AdminLogEntry[]>([])
  const [selected, setSelected] = useState<AdminLogEntry | null>(null)
  const connection = connectionMeta(stream.connectionState)

  useEffect(() => {
    setMerged([])
  }, [scopeKey])

  const matchesCurrentFilters = (log: AdminLogEntry) => {
    if (level !== 'All' && log.level !== level) return false
    if (correlationId && log.correlationId !== correlationId) return false
    if (workerNodeId && log.workerNodeId !== workerNodeId) return false
    if (deploymentTicketId && log.deploymentTicketId !== deploymentTicketId) return false
    if (eventCode && log.eventCode !== eventCode) return false
    if (resourceType && log.resourceType !== resourceType) return false
    if (resourceId && log.resourceId !== resourceId) return false
    if (statusFilter && log.status !== statusFilter) return false
    const normalizedIdentity = identity.trim().toLowerCase()
    if (normalizedIdentity && ![log.name, log.ip].filter(Boolean).some((value) => value?.toLowerCase().includes(normalizedIdentity))) {
      return false
    }
    const normalizedQuery = query.trim().toLowerCase()
    if (
      normalizedQuery &&
      ![log.msg, log.eventCode, log.resourceDisplayName, log.resourceId, log.workerNodeName]
        .filter(Boolean)
        .some((value) => value?.toLowerCase().includes(normalizedQuery))
    ) {
      return false
    }
    return true
  }

  const isCursorPage = logsRequest.logs?.contract === 'cursor'
  const activePage = isCursorPage ? cursorState.page : queryState.page
  const isFirstPage = activePage === 1
  const history = logsRequest.logs?.items ?? []
  const rows = useMemo(
    () => (isFirstPage ? mergeAdminLogs(merged, history) : history).filter(matchesCurrentFilters),
    [correlationId, deploymentTicketId, eventCode, history, identity, isFirstPage, level, merged, query, resourceId, resourceType, statusFilter, workerNodeId]
  )
  const buffered = useMemo(() => stream.buffered.filter(matchesCurrentFilters), [
    correlationId,
    deploymentTicketId,
    eventCode,
    identity,
    level,
    query,
    resourceId,
    resourceType,
    statusFilter,
    stream.buffered,
    workerNodeId,
  ])
  const metrics = useMemo(
    () => ({
      visible: rows.length,
      errors: rows.filter((log) => log.level?.toLowerCase() === 'error').length,
      warnings: rows.filter((log) => ['warning', 'warn'].includes(log.level?.toLowerCase() ?? '')).length,
    }),
    [rows]
  )

  const columns: AdminDataColumn<AdminLogEntry>[] = [
    {
      id: 'time',
      header: '时间',
      width: 'medium',
      render: (log) => <time className={styles.mono}>{formatAdminDate(log.time)}</time>,
    },
    {
      id: 'level',
      header: '级别',
      width: 'compact',
      render: (log) => {
        const meta = adminLogLevelMeta(log.level)
        return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
      },
    },
    {
      id: 'source',
      header: '用户 / 节点',
      width: 'medium',
      visibility: 'desktop',
      render: (log) => <span className={styles.ellipsis}>{adminLogSource(log)}</span>,
    },
    {
      id: 'resource',
      header: '事件 / 资源',
      width: 'wide',
      visibility: 'wide',
      render: (log) => (
        <div className={styles.resourceCell}>
          <strong>{log.eventCode || '系统日志'}</strong>
          <small>{adminLogResource(log)}</small>
        </div>
      ),
    },
    {
      id: 'message',
      header: '消息',
      width: 'wide',
      render: (log) => <span className={styles.message}>{log.msg || '—'}</span>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      visibility: 'desktop',
      render: (log) => {
        if (!log.status) return '—'
        const meta = adminLogStatusMeta(log.status)
        return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
      },
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (log) => (
        <button aria-label="查看日志详情" className={styles.iconButton} onClick={() => setSelected(log)} type="button">
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const mergeBuffered = () => {
    const keys = new Set(buffered.map(adminLogKey))
    setMerged((current) => mergeAdminLogs(buffered, current))
    stream.consume(keys)
    cursorState.reset()
    queryState.setPage(1)
  }

  const clearScope = () => {
    cursorState.reset()
    queryState.update({
      correlationId: null,
      workerNodeId: null,
      deploymentTicketId: null,
      eventCode: null,
      resourceType: null,
      resourceId: null,
    })
  }

  const hasServerScope = Boolean(correlationId || workerNodeId || deploymentTicketId || eventCode || resourceType || resourceId)

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            {stream.connectionState === 'disconnected' ? (
              <ActionButton icon={<RotateCw size={16} />} onClick={() => void stream.retry()} type="button">
                重连实时日志
              </ActionButton>
            ) : null}
            <ActionButton icon={<RefreshCw size={16} />} onClick={() => void logsRequest.mutate()} type="button">
              刷新历史
            </ActionButton>
          </>
        }
        description="查询持久化系统日志，并以手动合并方式接收实时运行事件。"
        eyebrow="OBSERVABILITY"
        title="系统日志"
      />

      <MetricStrip>
        <MetricItem
          detail="SignalR /hub/admin"
          label="实时通道"
          tone={connection.tone}
          value={connection.label}
        />
        <MetricItem detail="当前筛选结果" label="本页记录" value={metrics.visible} />
        <MetricItem detail="等待手动合并" label="新日志" tone={buffered.length ? 'info' : 'neutral'} value={buffered.length} />
        <MetricItem detail="当前页" label="错误" tone={metrics.errors ? 'danger' : 'neutral'} value={metrics.errors} />
        <MetricItem detail="当前页" label="警告" tone={metrics.warnings ? 'warning' : 'neutral'} value={metrics.warnings} />
      </MetricStrip>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索当前页日志消息"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder="当前页消息、事件或资源"
              type="search"
              value={query}
            />
          </label>
          <input
            aria-label="筛选当前页用户或 IP"
            className={styles.compactInput}
            onChange={(event) => setIdentity(event.currentTarget.value)}
            placeholder="当前页用户 / IP"
            value={identity}
          />
          <select
            aria-label="日志级别"
            onChange={(event) => {
              cursorState.reset()
              queryState.update({ level: event.currentTarget.value === 'All' ? null : event.currentTarget.value })
            }}
            value={level}
          >
            <option value="All">全部级别</option>
            <option value="Information">Information</option>
            <option value="Warning">Warning</option>
            <option value="Error">Error</option>
          </select>
          <select
            aria-label="当前页任务状态"
            onChange={(event) => queryState.update({ status: event.currentTarget.value || null })}
            value={statusFilter}
          >
            <option value="">全部状态</option>
            <option value="Success">Success</option>
            <option value="Failed">Failed</option>
            <option value="Pending">Pending</option>
            <option value="Degraded">Degraded</option>
            <option value="Exit">Exit</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={logsRequest.isRefreshing} label="历史记录按需刷新" />
      </FilterToolbar>

      {hasServerScope ? (
        <div className={styles.scopeBar}>
          <span>当前正在按关联运行上下文查询日志。</span>
          <button onClick={clearScope} type="button">
            清除关联筛选
          </button>
        </div>
      ) : null}

      {buffered.length ? (
        <div className={styles.bufferBar}>
          <span>
            <ListPlus size={16} />
            有 {buffered.length} 条符合当前条件的新日志，不会自动打断阅读位置。
          </span>
          <ActionButton onClick={mergeBuffered} tone="primary" type="button">
            合并到第一页
          </ActionButton>
        </div>
      ) : null}
      {stream.dropped ? <InlineFeedback tone="danger">实时缓冲已满，已丢弃 {stream.dropped} 条最旧日志。</InlineFeedback> : null}
      {stream.connectionState === 'disconnected' ? (
        <InlineFeedback tone="danger">
          <WifiOff size={15} /> 实时通道离线，持久化历史日志仍可查询。
        </InlineFeedback>
      ) : stream.connectionState === 'connected' ? (
        <span className={styles.connectionHint}>
          <Wifi size={14} /> 实时通道已连接
        </span>
      ) : null}

      {logsRequest.isLoading ? (
        <DataState description="正在读取持久化系统日志。" loading title="系统日志加载中" />
      ) : logsRequest.error ? (
        <DataState description="日志查询接口暂时不可用，实时通道状态不会替代持久化记录。" title="系统日志加载失败" />
      ) : (
        <>
          <DataTable
            caption="系统日志列表"
            columns={columns}
            emptyDescription="调整级别、关联条件或当前页筛选后重试。"
            emptyTitle="没有符合条件的系统日志"
            onRowClick={setSelected}
            rowKey={adminLogKey}
            rows={rows}
          />
          {isCursorPage ? (
            <CursorPaginationBar
              hasNext={Boolean(logsRequest.logs?.nextCursor)}
              onNext={() => logsRequest.logs?.nextCursor && cursorState.next(logsRequest.logs.nextCursor)}
              onPrevious={cursorState.previous}
              page={cursorState.page}
            />
          ) : logsRequest.logs ? (
            <CursorPaginationBar
              hasNext={logsRequest.logs.items.length === PAGE_SIZE}
              label="分页"
              onNext={() => queryState.setPage(queryState.page + 1)}
              onPrevious={() => queryState.setPage(Math.max(1, queryState.page - 1))}
              page={queryState.page}
            />
          ) : null}
        </>
      )}

      <AdminLogDrawer log={selected} onClose={() => setSelected(null)} />
    </div>
  )
}
