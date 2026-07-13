import {
  ActionIcon,
  Badge,
  Group,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiFilterOffOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import cx from 'clsx'
import dayjs from 'dayjs'
import { memo, useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import tableClasses from '@Styles/Table.module.css'
import { fetchOperationalEvents } from './api'
import { OperationalEventDetailDrawer } from './OperationalEventDetailDrawer'
import { OperationalEventFilters, OperationalEventItem } from './types'

const PAGE_SIZE = 50
const domainOptions = ['runtime', 'capacity', 'image', 'node', 'agent', 'container', 'vm', 'teamlab', 'recovery', 'audit']
  .map((value) => ({ value, label: value }))
const outcomeOptions = ['Started', 'Pending', 'Blocked', 'Succeeded', 'Failed', 'Cancelled', 'Recovered', 'Observed']
  .map((value) => ({ value, label: value }))
const errorOptions = [
  'Authorization', 'Validation', 'Conflict', 'Scheduling', 'Capacity', 'ImageRegistry', 'ImageTransfer',
  'NodeUnavailable', 'AgentProtocol', 'AgentTransport', 'Docker', 'Kvm', 'Network', 'HealthCheck',
  'Storage', 'Database', 'Cache', 'Unknown',
].map((value) => ({ value, label: value }))

function outcomeMeta(value: string | number) {
  const key = String(value).toLowerCase()
  if (key === '3' || key === 'succeeded' || key === '6' || key === 'recovered') return { color: 'green', semantic: 'success' }
  if (key === '4' || key === 'failed') return { color: 'red', semantic: 'failed' }
  if (key === '1' || key === 'pending' || key === '2' || key === 'blocked') return { color: 'orange', semantic: 'pending' }
  if (key === '5' || key === 'cancelled') return { color: 'gray', semantic: 'canceled' }
  return { color: 'blue', semantic: 'observed' }
}

function primaryObject(item: OperationalEventItem) {
  const labels = item.labels
  return labels.subject || labels.resource || labels.game || labels.course || labels.team || '-'
}

const EventRow = memo(function EventRow({ item, onSelect }: { item: OperationalEventItem; onSelect: (item: OperationalEventItem) => void }) {
  const outcome = outcomeMeta(item.event.outcome)
  return (
    <Table.Tr onClick={() => onSelect(item)} className={cx(tableClasses.clickable, tableClasses.virtual)}>
      <Table.Td><Text size="xs" ff="monospace">{dayjs(item.event.occurredAt).format('MM-DD HH:mm:ss.SSS')}</Text></Table.Td>
      <Table.Td>
        <Stack gap={2}>
          <Badge size="xs" variant="light">{item.domain}</Badge>
          <Text size="xs" ff="monospace" lineClamp={1}>{item.event.eventCode}</Text>
        </Stack>
      </Table.Td>
      <Table.Td><Text size="sm" fw={700} lineClamp={2}>{primaryObject(item)}</Text></Table.Td>
      <Table.Td><Text size="xs" lineClamp={2}>{item.labels.workerNode || '-'}</Text></Table.Td>
      <Table.Td>
        <Badge color={outcome.color} size="sm" className="yy-semantic-badge" data-semantic={outcome.semantic}>
          {String(item.event.outcome)}
        </Badge>
      </Table.Td>
      <Table.Td><Text size="sm" lineClamp={2}>{item.event.message}</Text></Table.Td>
    </Table.Tr>
  )
})

export function OperationalEventsView({
  recoveryOnly = false,
  initialFilters = {},
}: {
  recoveryOnly?: boolean
  initialFilters?: OperationalEventFilters
}) {
  const [domain, setDomain] = useState<string | null>(recoveryOnly ? 'recovery' : null)
  const [outcome, setOutcome] = useState<string | null>(null)
  const [errorCategory, setErrorCategory] = useState<string | null>(null)
  const [correlationId, setCorrelationId] = useState(initialFilters.correlationId ?? '')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [cursorStack, setCursorStack] = useState<string[]>([])
  const [selected, setSelected] = useState<OperationalEventItem | null>(null)
  const cursor = cursorStack.at(-1)

  useEffect(() => {
    setCorrelationId(initialFilters.correlationId ?? '')
    setCursorStack([])
  }, [initialFilters.correlationId, initialFilters.workerNodeId, initialFilters.imageTemplateId, initialFilters.deploymentTicketId])

  const normalizedCorrelation = correlationId.trim()
  const correlationInvalid = normalizedCorrelation.length > 0 &&
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(normalizedCorrelation)
  const url = useMemo(() => {
    if (correlationInvalid) return null
    const params = new URLSearchParams({ count: PAGE_SIZE.toString() })
    if (cursor) params.set('cursor', cursor)
    if (domain && !recoveryOnly) params.set('domain', domain)
    if (outcome) params.set('outcome', outcome)
    if (errorCategory) params.set('errorCategory', errorCategory)
    if (normalizedCorrelation) params.set('correlationId', normalizedCorrelation)
    if (from && dayjs(from).isValid()) params.set('from', dayjs(from).toISOString())
    if (to && dayjs(to).isValid()) params.set('to', dayjs(to).toISOString())
    if (initialFilters.workerNodeId) params.set('workerNodeId', initialFilters.workerNodeId)
    if (initialFilters.imageTemplateId) params.set('imageTemplateId', initialFilters.imageTemplateId)
    if (initialFilters.deploymentTicketId) params.set('deploymentTicketId', initialFilters.deploymentTicketId)
    const endpoint = recoveryOnly ? 'recovery' : 'events'
    return `/api/admin/operations/${endpoint}?${params.toString()}`
  }, [correlationInvalid, cursor, domain, errorCategory, from, initialFilters.deploymentTicketId, initialFilters.imageTemplateId, initialFilters.workerNodeId, normalizedCorrelation, outcome, recoveryOnly, to])
  const { data, error, isLoading, mutate } = useSWR(url, fetchOperationalEvents, {
    keepPreviousData: true,
    refreshInterval: cursor ? 0 : 5000,
  })
  const items = data?.items ?? []

  const reset = () => {
    setDomain(recoveryOnly ? 'recovery' : null)
    setOutcome(null)
    setErrorCategory(null)
    setCorrelationId('')
    setFrom('')
    setTo('')
    setCursorStack([])
  }

  return (
    <Stack gap="md" w="100%">
      <Group justify="space-between" align="end" wrap="wrap">
        <Group align="end" wrap="wrap">
          <TextInput
            label="Correlation"
            placeholder="UUID"
            value={correlationId}
            onChange={(event) => {
              setCorrelationId(event.currentTarget.value)
              setCursorStack([])
            }}
            w={310}
          />
          <TextInput label="起始时间" type="datetime-local" value={from} onChange={(event) => { setFrom(event.currentTarget.value); setCursorStack([]) }} w={205} />
          <TextInput label="结束时间" type="datetime-local" value={to} onChange={(event) => { setTo(event.currentTarget.value); setCursorStack([]) }} w={205} />
          {!recoveryOnly ? <Select label="领域" clearable data={domainOptions} value={domain} onChange={(value) => { setDomain(value); setCursorStack([]) }} w={150} /> : null}
          <Select label="结果" clearable data={outcomeOptions} value={outcome} onChange={(value) => { setOutcome(value); setCursorStack([]) }} w={150} />
          <Select label="错误分类" clearable searchable data={errorOptions} value={errorCategory} onChange={(value) => { setErrorCategory(value); setCursorStack([]) }} w={190} />
        </Group>
        <Group gap="xs">
          <Tooltip label="清除筛选"><ActionIcon variant="default" onClick={reset}><Icon path={mdiFilterOffOutline} size={0.85} /></ActionIcon></Tooltip>
          <Tooltip label="刷新"><ActionIcon variant="default" onClick={() => mutate()}><Icon path={mdiRefresh} size={0.85} /></ActionIcon></Tooltip>
        </Group>
      </Group>

      <YinyuTableShell p={0} w="100%">
        <ScrollArea h="calc(100vh - 290px)" offsetScrollbars scrollbarSize={4}>
          <Table miw={1120} highlightOnHover stickyHeader>
            <Table.Thead><Table.Tr>
              <Table.Th w={150}>时间</Table.Th><Table.Th w={240}>事件</Table.Th><Table.Th w={180}>对象</Table.Th>
              <Table.Th w={150}>节点</Table.Th><Table.Th w={110}>结果</Table.Th><Table.Th>消息</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {items.length === 0 ? <Table.Tr><Table.Td colSpan={6} ta="center"><Text c="dimmed" py="xl">{correlationInvalid ? 'Correlation 必须是有效 UUID' : error ? error.message : isLoading ? '加载中...' : '暂无匹配事件'}</Text></Table.Td></Table.Tr> : null}
              {items.map((item) => <EventRow key={item.event.id} item={item} onSelect={setSelected} />)}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </YinyuTableShell>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">第 {cursorStack.length + 1} 页</Text>
        <Group gap="xs">
          <ActionIcon aria-label="上一页" disabled={cursorStack.length === 0} onClick={() => setCursorStack((current) => current.slice(0, -1))}><Icon path={mdiArrowLeftBold} size={0.85} /></ActionIcon>
          <ActionIcon aria-label="下一页" disabled={!data?.nextCursor} onClick={() => data?.nextCursor && setCursorStack((current) => [...current, data.nextCursor!])}><Icon path={mdiArrowRightBold} size={0.85} /></ActionIcon>
        </Group>
      </Group>

      <OperationalEventDetailDrawer item={selected} onClose={() => setSelected(null)} />
    </Stack>
  )
}
