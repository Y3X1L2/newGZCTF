import { Eye, ListPlus, RefreshCw, RotateCw, Search } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import type { AdminLogEntry } from '../api'
import {
  AdminPageHeader,
  CursorPaginationBar,
  DataTable,
  FilterToolbar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import { useAdminCursorState } from '../shared/useAdminCursorState'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import { AdminLogDrawer } from './AdminLogDrawer'
import styles from './AdminLogsPage.module.css'
import {
  adminLogKey,
  adminLogActor,
  adminLogMatches,
  adminLogObject,
  adminLogResult,
  adminLogHasDiagnostics,
  adminLogTitle,
  mergeAdminLogs,
} from './adminLogPresentation'
import { useAdminLogStream } from './useAdminLogStream'
import { useAdminLogs } from './useAdminLogs'

const PAGE_SIZE = 50

export function AdminLogsPage() {
  const { t, i18n } = useTranslation('admin')
  const text = (key: string) => t(`logs.${key}`)
  const queryState = useAdminQueryState(PAGE_SIZE)
  const level = queryState.params.get('level') ?? 'All'
  const statusFilter = queryState.params.get('status') ?? ''
  const correlationId = queryState.params.get('correlationId') || undefined
  const workerNodeId = queryState.params.get('workerNodeId') || undefined
  const deploymentTicketId = queryState.params.get('deploymentTicketId') || undefined
  const eventCode = queryState.params.get('eventCode') || undefined
  const resourceType = queryState.params.get('resourceType') || undefined
  const resourceId = queryState.params.get('resourceId') || undefined
  const scopeKey = [level, correlationId, workerNodeId, deploymentTicketId, eventCode, resourceType, resourceId].join(
    '|'
  )
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
  const connection = {
    label: text(`connection.${stream.connectionState}`),
    tone:
      stream.connectionState === 'connected'
        ? ('success' as const)
        : stream.connectionState === 'disconnected'
          ? ('danger' as const)
          : ('info' as const),
  }

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
    if (
      statusFilter &&
      adminLogResult(log, text).label !== adminLogResult({ ...log, outcome: statusFilter, status: null }, text).label
    )
      return false
    return adminLogMatches(log, query, identity, text)
  }

  const isCursorPage = logsRequest.logs?.contract === 'cursor'
  const activePage = isCursorPage ? cursorState.page : queryState.page
  const isFirstPage = activePage === 1
  const history = logsRequest.logs?.items ?? []
  const rows = useMemo(
    () => (isFirstPage ? mergeAdminLogs(merged, history) : history).filter(matchesCurrentFilters),
    [
      correlationId,
      deploymentTicketId,
      eventCode,
      history,
      identity,
      isFirstPage,
      level,
      merged,
      query,
      resourceId,
      resourceType,
      statusFilter,
      workerNodeId,
      i18n.language,
    ]
  )
  const buffered = useMemo(
    () => stream.buffered.filter(matchesCurrentFilters),
    [
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
      i18n.language,
      workerNodeId,
    ]
  )
  const metrics = useMemo(
    () => ({
      visible: rows.length,
      errors: rows.filter(adminLogHasDiagnostics).length,
      warnings: rows.filter((log) => ['warning', 'warn'].includes(log.level?.toLowerCase() ?? '')).length,
    }),
    [rows]
  )

  const columns: AdminDataColumn<AdminLogEntry>[] = [
    {
      id: 'time',
      header: text('time'),
      width: 'medium',
      visibility: 'desktop',
      render: (log) => <time className={styles.mono}>{formatAdminDate(log.time)}</time>,
    },
    {
      id: 'actor',
      header: text('actor_label'),
      width: 'medium',
      visibility: 'desktop',
      render: (log) => <span className={styles.actor}>{adminLogActor(log, text)}</span>,
    },
    {
      id: 'event',
      header: text('event_label'),
      render: (log) => {
        const result = adminLogResult(log, text)
        return (
          <div className={styles.eventCell}>
            <strong>{adminLogTitle(log, text)}</strong>
            <span>{adminLogObject(log, text)}</span>
            <div className={styles.mobileContext}>
              <time>{formatAdminDate(log.time)}</time>
              <span>{adminLogActor(log, text)}</span>
              <StatusBadge tone={result.tone}>{result.label}</StatusBadge>
            </div>
          </div>
        )
      },
    },
    {
      id: 'result',
      header: text('result_label'),
      width: 'compact',
      visibility: 'desktop',
      render: (log) => {
        const result = adminLogResult(log, text)
        return <StatusBadge tone={result.tone}>{result.label}</StatusBadge>
      },
    },
    {
      id: 'action',
      header: text('details_column'),
      width: 'compact',
      align: 'right',
      render: (log) => (
        <button
          aria-label={`${text('details')}: ${adminLogTitle(log, text)}`}
          className={styles.iconButton}
          onClick={() => setSelected(log)}
          type="button"
        >
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

  const hasServerScope = Boolean(
    correlationId || workerNodeId || deploymentTicketId || eventCode || resourceType || resourceId
  )

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            {stream.connectionState === 'disconnected' ? (
              <ActionButton icon={<RotateCw size={16} />} onClick={() => void stream.retry()} type="button">
                {text('reconnect')}
              </ActionButton>
            ) : null}
            <ActionButton icon={<RefreshCw size={16} />} onClick={() => void logsRequest.mutate()} type="button">
              {text('refresh')}
            </ActionButton>
          </>
        }
        description={text('description')}
        eyebrow={text('eyebrow')}
        title={text('title')}
      />

      <div className={styles.summaryLine}>
        <span>
          {t('logs.summary_counts', { count: metrics.visible, errors: metrics.errors, warnings: metrics.warnings })}
        </span>
        <StatusBadge tone={connection.tone}>{connection.label}</StatusBadge>
      </div>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label={text('search')}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder={text('search_placeholder')}
              type="search"
              value={query}
            />
          </label>
          <input
            aria-label={text('identity')}
            className={styles.compactInput}
            onChange={(event) => setIdentity(event.currentTarget.value)}
            placeholder={text('identity_placeholder')}
            value={identity}
          />
          <select
            aria-label={text('level_label')}
            onChange={(event) => {
              cursorState.reset()
              queryState.update({ level: event.currentTarget.value === 'All' ? null : event.currentTarget.value })
            }}
            value={level}
          >
            <option value="All">{text('all_levels')}</option>
            <option value="Information">{text('level.information')}</option>
            <option value="Warning">{text('level.warning')}</option>
            <option value="Error">{text('level.error')}</option>
          </select>
          <select
            aria-label={text('result_filter')}
            onChange={(event) => queryState.update({ status: event.currentTarget.value || null }, { resetPage: false })}
            value={statusFilter}
          >
            <option value="">{text('all_results')}</option>
            {['Started', 'Pending', 'Blocked', 'Succeeded', 'Failed', 'Cancelled', 'Recovered', 'Observed'].map(
              (value) => (
                <option key={value} value={value}>
                  {text(`result.${value.toLowerCase()}`)}
                </option>
              )
            )}
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={logsRequest.isRefreshing} label={text('refresh_hint')} />
      </FilterToolbar>

      <p className={styles.filterHint}>{text('filter_hint')}</p>

      {hasServerScope ? (
        <div className={styles.scopeBar}>
          <span>{text('scope')}</span>
          <button onClick={clearScope} type="button">
            {text('clear_scope')}
          </button>
        </div>
      ) : null}

      {buffered.length ? (
        <div className={styles.bufferBar}>
          <span>
            <ListPlus size={16} />
            {t('logs.buffer', { count: buffered.length })}
          </span>
          <ActionButton onClick={mergeBuffered} tone="primary" type="button">
            {text('merge')}
          </ActionButton>
        </div>
      ) : null}
      {stream.dropped ? (
        <InlineFeedback tone="danger">{t('logs.dropped', { count: stream.dropped })}</InlineFeedback>
      ) : null}
      {stream.connectionState === 'disconnected' ? (
        <InlineFeedback tone="danger">{text('offline')}</InlineFeedback>
      ) : null}

      {logsRequest.isLoading ? (
        <DataState description={text('loading_description')} loading title={text('loading')} />
      ) : logsRequest.error ? (
        <DataState description={text('load_failed_description')} title={text('load_failed')} />
      ) : (
        <>
          <DataTable
            caption={text('list')}
            columns={columns}
            emptyDescription={text('empty_description')}
            emptyTitle={text('empty')}
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
              label={text('pagination')}
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
