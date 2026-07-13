import { ActionIcon, Badge, Box, Button, Group, Select, Stack, Table, Text, Title, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiClose, mdiOpenInNew, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import useSWR from 'swr'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'

interface DeploymentQueueItem {
  id: string
  correlationId: string
  typeLabel: string
  actionLabel: string
  requestLabel: string
  image?: string | null
  targetNodeLabel: string
  statusLabel: string
  statusKey: string
  dockerSlots: number
  vmSlots: number
  peopleAhead: number
  stageMessage?: string | null
  blockedReasonCode?: string | null
  errorMessage?: string | null
  createdAt: string
}

interface DeploymentQueueResponse { items: DeploymentQueueItem[]; nextCursor?: string | null }

const PAGE_SIZE = 20
const activeStatuses = new Set(['pending', 'scheduling', 'scheduled', 'running'])
const statusConfig: Record<string, { color: string; semantic: string }> = {
  pending: { color: 'violet', semantic: 'pending' }, scheduling: { color: 'cyan', semantic: 'running' },
  scheduled: { color: 'blue', semantic: 'assigned' }, running: { color: 'blue', semantic: 'running' },
  completed: { color: 'green', semantic: 'success' }, failed: { color: 'red', semantic: 'failed' },
  cancelled: { color: 'gray', semantic: 'canceled' },
}
const statusOptions = [
  { value: 'pending', label: '等待中' }, { value: 'scheduling', label: '调度中' },
  { value: 'scheduled', label: '已分配' }, { value: 'running', label: '执行中' },
  { value: 'completed', label: '已完成' }, { value: 'failed', label: '失败' },
  { value: 'cancelled', label: '已取消' },
]

async function fetcher(url: string) {
  const response = await fetch(url)
  if (!response.ok) throw new Error('Failed to load deployment queue')
  return (await response.json()) as DeploymentQueueResponse
}

function slotsLabel(item: DeploymentQueueItem) {
  const parts: string[] = []
  if (item.dockerSlots > 0) parts.push(`Docker ${item.dockerSlots}`)
  if (item.vmSlots > 0) parts.push(`VM ${item.vmSlots}`)
  return parts.length > 0 ? parts.join(' / ') : '-'
}

function formatTime(value?: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

export function DeploymentQueueView({ showHeader = true }: { showHeader?: boolean }) {
  const [statusFilter, setStatusFilter] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<string[]>([])
  const cursor = cursorStack.at(-1)
  const page = cursorStack.length + 1
  const query = useMemo(() => {
    const params = new URLSearchParams({ pageSize: PAGE_SIZE.toString() })
    if (statusFilter) params.set('status', statusFilter)
    if (cursor) params.set('cursor', cursor)
    return `/api/v1/deployment-queue?${params.toString()}`
  }, [cursor, statusFilter])
  const { data, isLoading, mutate } = useSWR(query, fetcher, {
    refreshInterval: (latest) => latest?.items.some((item) => activeStatuses.has(item.statusKey)) ? 1500 : 10000,
    keepPreviousData: true,
  })

  const handleCancel = async (id: string) => {
    try {
      const response = await fetch(`/api/v1/deployment-queue/${id}`, { method: 'DELETE' })
      if (!response.ok) {
        const body = await response.json().catch(() => ({}))
        notifications.show({ title: '取消失败', message: body.message || '请检查任务状态', color: 'red' })
        return
      }
      notifications.show({ title: '已取消', message: '部署任务已取消', color: 'green' })
      await mutate()
    } catch {
      notifications.show({ title: '取消失败', message: '网络错误', color: 'red' })
    }
  }

  const items = data?.items ?? []
  return (
    <Stack data-testid="queue-view" gap="lg" w="100%">
      {showHeader ? (
        <Group justify="space-between" wrap="nowrap" className="yy-admin-page-head">
          <div><Title order={2}>部署队列</Title><Text size="sm" className="yy-readable-text">查看环境生命周期任务的排队、调度和执行进度。</Text></div>
          <Group wrap="nowrap">
            <Select placeholder="筛选状态" clearable data={statusOptions} value={statusFilter} onChange={(value) => { setStatusFilter(value); setCursorStack([]) }} w={140} />
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.85} />} onClick={() => mutate()}>刷新</Button>
          </Group>
        </Group>
      ) : (
        <Group justify="space-between" align="end">
          <Select label="状态" placeholder="全部" clearable data={statusOptions} value={statusFilter} onChange={(value) => { setStatusFilter(value); setCursorStack([]) }} w={160} />
          <Tooltip label="刷新"><ActionIcon variant="default" onClick={() => mutate()}><Icon path={mdiRefresh} size={0.85} /></ActionIcon></Tooltip>
        </Group>
      )}

      <YinyuTableShell p={0} w="100%" style={{ overflow: 'hidden' }}>
        <Box style={{ overflowX: 'auto' }}>
          <Table miw={1260} highlightOnHover>
            <Table.Thead><Table.Tr>
              <Table.Th>请求对象</Table.Th><Table.Th>镜像/模板</Table.Th><Table.Th>目标节点</Table.Th>
              <Table.Th>类型</Table.Th><Table.Th>操作</Table.Th><Table.Th>状态</Table.Th>
              <Table.Th>当前阶段</Table.Th><Table.Th>资源</Table.Th><Table.Th>创建时间</Table.Th><Table.Th>管理</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {items.length === 0 ? <Table.Tr><Table.Td colSpan={10} ta="center"><Text c="dimmed" py="xl">{isLoading ? '加载中...' : '暂无部署任务'}</Text></Table.Td></Table.Tr> : null}
              {items.map((item) => {
                const status = statusConfig[item.statusKey] ?? { color: 'gray', semantic: 'unknown' }
                const detail = item.errorMessage || item.stageMessage || item.blockedReasonCode || ''
                return (
                  <Table.Tr key={item.id} style={{ contentVisibility: 'auto' }}>
                    <Table.Td><Stack gap={2}><Text size="sm" fw={700} lineClamp={2}>{item.requestLabel}</Text><Text size="xs" c="dimmed">任务 {item.id.slice(0, 8)}</Text></Stack></Table.Td>
                    <Table.Td><Text size="xs" lineClamp={2}>{item.image || '-'}</Text></Table.Td>
                    <Table.Td><Text size="xs" lineClamp={2}>{item.targetNodeLabel}</Text></Table.Td>
                    <Table.Td>{item.typeLabel}</Table.Td><Table.Td>{item.actionLabel}</Table.Td>
                    <Table.Td><Tooltip label={detail} disabled={!detail}><Badge color={status.color} size="sm" className="yy-semantic-badge" data-semantic={status.semantic}>{item.statusLabel}</Badge></Tooltip>{item.peopleAhead > 0 ? <Text size="xs" c="dimmed" mt={4}>前方 {item.peopleAhead} 个任务</Text> : null}</Table.Td>
                    <Table.Td><Text size="xs" lineClamp={3}>{item.stageMessage || '-'}</Text></Table.Td>
                    <Table.Td><Text size="xs">{slotsLabel(item)}</Text></Table.Td>
                    <Table.Td><Text size="xs">{formatTime(item.createdAt)}</Text></Table.Td>
                    <Table.Td><Group gap={4} wrap="nowrap">
                      <Tooltip label="关联时间线"><ActionIcon component={Link} to={`/admin/logs?tab=events&correlation=${encodeURIComponent(item.correlationId || item.id)}`} variant="subtle" size="sm"><Icon path={mdiOpenInNew} size={0.78} /></ActionIcon></Tooltip>
                      {activeStatuses.has(item.statusKey) ? <Tooltip label="取消任务"><ActionIcon color="red" variant="subtle" size="sm" onClick={() => handleCancel(item.id)}><Icon path={mdiClose} size={0.82} /></ActionIcon></Tooltip> : null}
                    </Group></Table.Td>
                  </Table.Tr>
                )
              })}
            </Table.Tbody>
          </Table>
        </Box>
      </YinyuTableShell>

      {(items.length > 0 || page > 1) ? <Group justify="space-between"><Text size="sm" c="dimmed">第 {page} 页</Text><Group gap="xs"><ActionIcon disabled={cursorStack.length === 0} onClick={() => setCursorStack((current) => current.slice(0, -1))}><Icon path={mdiArrowLeftBold} size={0.85} /></ActionIcon><ActionIcon disabled={!data?.nextCursor} onClick={() => data?.nextCursor && setCursorStack((current) => [...current, data.nextCursor!])}><Icon path={mdiArrowRightBold} size={0.85} /></ActionIcon></Group></Group> : null}
    </Stack>
  )
}
