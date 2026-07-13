import { ActionIcon, Badge, Group, ScrollArea, SegmentedControl, Stack, Table, Text, Tooltip, useMantineTheme } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import cx from 'clsx'
import dayjs from 'dayjs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { TaskStatusColorMap } from '@Utils/Shared'
import { LogMessageModel, TaskStatus } from '@Api'
import tableClasses from '@Styles/Table.module.css'
import { OperationalEventFilters } from './types'

const PAGE_SIZE = 50

enum LogLevel {
  Info = 'Information',
  Warn = 'Warning',
  Error = 'Error',
  All = 'All',
}

interface SystemLogItem extends LogMessageModel {
  correlationId?: string | null
  traceId?: string | null
  eventCode?: string | null
  errorCategory?: string | null
  errorCode?: string | null
  workerNodeId?: string | null
  workerNodeName?: string | null
  deploymentTicketId?: string | null
  resourceType?: string | null
  resourceId?: string | null
  resourceDisplayName?: string | null
}

interface SystemLogPage { items: SystemLogItem[]; nextCursor?: string | null }

function taskStatusSemantic(status?: TaskStatus | null) {
  switch (status) {
    case TaskStatus.Success: return 'success'
    case TaskStatus.Failed:
    case TaskStatus.Denied:
    case TaskStatus.Unhealthy: return 'failed'
    case TaskStatus.Pending: return 'pending'
    case TaskStatus.Degraded: return 'warning'
    case TaskStatus.Exit: return 'canceled'
    default: return 'unknown'
  }
}

function inScope(item: SystemLogItem, filters: OperationalEventFilters) {
  if (filters.correlationId && item.correlationId !== filters.correlationId) return false
  if (filters.workerNodeId && item.workerNodeId !== filters.workerNodeId) return false
  if (filters.deploymentTicketId && item.deploymentTicketId !== filters.deploymentTicketId) return false
  if (filters.imageTemplateId && (item.resourceType !== 'image-template' || item.resourceId !== filters.imageTemplateId)) return false
  return true
}

export function SystemLogView({ filters = {} }: { filters?: OperationalEventFilters }) {
  const [level, setLevel] = useState(LogLevel.Info)
  const [cursorStack, setCursorStack] = useState<string[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>()
  const [logs, setLogs] = useState<SystemLogItem[]>()
  const [refreshKey, setRefreshKey] = useState(0)
  const [, forceRender] = useState(0)
  const newLogs = useRef<SystemLogItem[]>([])
  const viewport = useRef<HTMLDivElement>(null)
  const cursor = cursorStack.at(-1)
  const page = cursorStack.length + 1
  const theme = useMantineTheme()
  const { t } = useTranslation()
  const { locale } = useLanguage()

  const query = useMemo(() => {
    const params = new URLSearchParams({ level, count: PAGE_SIZE.toString() })
    if (cursor) params.set('cursor', cursor)
    if (filters.correlationId) params.set('correlationId', filters.correlationId)
    if (filters.workerNodeId) params.set('workerNodeId', filters.workerNodeId)
    if (filters.deploymentTicketId) params.set('deploymentTicketId', filters.deploymentTicketId)
    if (filters.imageTemplateId) {
      params.set('resourceType', 'image-template')
      params.set('resourceId', filters.imageTemplateId)
    }
    return `/api/admin/logs?${params.toString()}`
  }, [cursor, filters.correlationId, filters.deploymentTicketId, filters.imageTemplateId, filters.workerNodeId, level])

  useEffect(() => {
    let active = true
    fetch(query)
      .then(async (response) => {
        if (!response.ok) throw new Error(`Request failed with ${response.status}`)
        return (await response.json()) as SystemLogPage
      })
      .then((data) => {
        if (!active) return
        setLogs(data.items)
        setNextCursor(data.nextCursor)
        if (page === 1) newLogs.current = []
      })
      .catch((error) => showNotification({ color: 'red', title: t('admin.notification.logs.fetch_failed'), message: String(error) }))
    return () => { active = false }
  }, [page, query, refreshKey, t])

  useEffect(() => {
    viewport.current?.scrollTo({ top: 0, behavior: 'smooth' })
  }, [page, level])

  useEffect(() => {
    setCursorStack([])
  }, [level, filters.correlationId, filters.workerNodeId, filters.deploymentTicketId, filters.imageTemplateId])

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()
    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 24
    connection.on('ReceivedLog', (message: SystemLogItem) => {
      if (!inScope(message, filters)) return
      newLogs.current = [message, ...newLogs.current].slice(0, PAGE_SIZE)
      forceRender((value) => value + 1)
    })
    connection.start().catch(() => undefined)
    return () => { connection.stop().catch(() => undefined) }
  }, [filters.correlationId, filters.deploymentTicketId, filters.imageTemplateId, filters.workerNodeId])

  const rows = [...(page === 1 ? newLogs.current : []), ...(logs ?? [])]
    .filter((item) => level === LogLevel.All || item.level === level)

  return (
    <Stack gap="md" w="100%">
      <Group justify="space-between">
        <SegmentedControl
          className="yy-admin-logs-filter"
          color={theme.primaryColor}
          value={level}
          bg="transparent"
          onChange={(value) => setLevel(value as LogLevel)}
          data={Object.entries(LogLevel).map(([label, value]) => ({ label, value }))}
        />
        <Group className="yy-admin-logs-pagination" gap="xs">
          <Tooltip label="刷新"><ActionIcon variant="default" onClick={() => setRefreshKey((value) => value + 1)}><Icon path={mdiRefresh} size={0.85} /></ActionIcon></Tooltip>
          <ActionIcon disabled={page <= 1} onClick={() => setCursorStack((current) => current.slice(0, -1))}><Icon path={mdiArrowLeftBold} size={0.85} /></ActionIcon>
          <Text fw="bold" size="sm">{page}</Text>
          <ActionIcon disabled={!nextCursor} onClick={() => nextCursor && setCursorStack((current) => [...current, nextCursor])}><Icon path={mdiArrowRightBold} size={0.85} /></ActionIcon>
        </Group>
      </Group>

      <YinyuTableShell p="md" w="100%" className="yy-admin-logs-page">
        <ScrollArea viewportRef={viewport} offsetScrollbars scrollbarSize={4} h="calc(100vh - 260px)">
          <Table className={cx(tableClasses.table, tableClasses.fixed)} miw={1180}>
            <Table.Thead><Table.Tr>
              <Table.Th w="9rem">{t('common.label.time')}</Table.Th>
              <Table.Th w="7rem">级别</Table.Th>
              <Table.Th w="11rem">节点/用户</Table.Th>
              <Table.Th w="15rem">事件/资源</Table.Th>
              <Table.Th>消息</Table.Th>
              <Table.Th w="7rem">{t('admin.label.logs.status')}</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {rows.map((item, index) => (
                <Table.Tr key={item.id ?? `${item.time}@${index}`} className={tableClasses.virtual}>
                  <Table.Td><Text ff="monospace" size="xs">{dayjs(item.time).locale(locale).format('MM-DD HH:mm:ss')}</Text></Table.Td>
                  <Table.Td><Badge size="xs" variant="light">{item.level || '-'}</Badge></Table.Td>
                  <Table.Td><Text size="xs" lineClamp={2}>{item.workerNodeName || item.name || item.ip || '-'}</Text></Table.Td>
                  <Table.Td><Stack gap={2}><Text size="xs" ff="monospace" lineClamp={1}>{item.eventCode || '-'}</Text><Text size="xs" c="dimmed" lineClamp={1}>{item.resourceDisplayName || item.resourceId || '-'}</Text></Stack></Table.Td>
                  <Table.Td><Text size="sm" lineClamp={3}>{item.msg || ''}</Text></Table.Td>
                  <Table.Td>{item.status ? <Badge size="sm" color={TaskStatusColorMap.get(item.status as TaskStatus) ?? 'gray'} className="yy-semantic-badge" data-semantic={taskStatusSemantic(item.status as TaskStatus)}>{item.status}</Badge> : null}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </YinyuTableShell>
    </Stack>
  )
}
