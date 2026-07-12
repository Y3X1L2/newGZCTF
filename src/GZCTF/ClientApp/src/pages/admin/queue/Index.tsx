import { ActionIcon, Badge, Box, Button, Group, Select, Stack, Table, Text, Title, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiClose, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useMemo, useState } from 'react'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'

interface DeploymentQueueItem {
  id: string
  ticketId?: string | null
  targetId?: string | null
  typeLabel: string
  actionLabel: string
  requestLabel: string
  ownerLabel?: string | null
  gameLabel?: string | null
  challengeLabel?: string | null
  image?: string | null
  targetNodeLabel: string
  statusLabel: string
  statusKey: string
  dockerSlots: number
  vmSlots: number
  queuePosition: number
  peopleAhead: number
  result?: string | null
  errorMessage?: string | null
  createdAt: string
  startedAt?: string | null
  completedAt?: string | null
}

interface DeploymentQueueResponse {
  items: DeploymentQueueItem[]
  nextCursor?: string | null
}

const PAGE_SIZE = 20

const fetcher = async (url: string) => {
  const res = await fetch(url)
  if (!res.ok) throw new Error('Failed to load deployment queue')
  return (await res.json()) as DeploymentQueueResponse
}

const statusConfig: Record<string, { color: string; semantic: string }> = {
  pending: { color: 'violet', semantic: 'pending' },
  assigned: { color: 'blue', semantic: 'assigned' },
  running: { color: 'blue', semantic: 'running' },
  completed: { color: 'green', semantic: 'success' },
  failed: { color: 'red', semantic: 'failed' },
  cancelled: { color: 'gray', semantic: 'canceled' },
}

const statusOptions = [
  { value: 'pending', label: '等待中' },
  { value: 'assigned', label: '已分配' },
  { value: 'running', label: '执行中' },
  { value: 'completed', label: '已完成' },
  { value: 'failed', label: '失败' },
  { value: 'cancelled', label: '已取消' },
]

function formatTime(value?: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

function slotsLabel(item: DeploymentQueueItem) {
  const parts = []
  if (item.dockerSlots > 0) parts.push(`Docker ${item.dockerSlots}`)
  if (item.vmSlots > 0) parts.push(`VM ${item.vmSlots}`)
  return parts.length > 0 ? parts.join(' / ') : '-'
}

export default function QueuePage() {
  const [statusFilter, setStatusFilter] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<string[]>([])
  const cursor = cursorStack.at(-1)
  const page = cursorStack.length + 1
  const query = useMemo(() => {
    const params = new URLSearchParams({
      pageSize: PAGE_SIZE.toString(),
    })
    if (statusFilter) params.set('status', statusFilter)
    if (cursor) params.set('cursor', cursor)
    return `/api/v1/deployment-targets?${params.toString()}`
  }, [cursor, statusFilter])
  const { data, isLoading, mutate } = useSWR(query, fetcher, {
    refreshInterval: 10000,
    keepPreviousData: true,
  })

  const handleCancel = async (id: string) => {
    try {
      const res = await fetch(`/api/v1/deployment-targets/${id}`, { method: 'DELETE' })
      if (res.ok) {
        notifications.show({ title: '已取消', message: '部署任务已取消', color: 'green' })
        await mutate()
      } else {
        const body = await res.json().catch(() => ({}))
        notifications.show({ title: '取消失败', message: body.message || '请检查任务状态', color: 'red' })
      }
    } catch {
      notifications.show({ title: '取消失败', message: '网络错误', color: 'red' })
    }
  }

  const items = data?.items ?? []
  const nextCursor = data?.nextCursor

  return (
    <AdminPage>
      <Stack data-testid="queue-page" gap="lg" w="100%">
        <Group justify="space-between" mb="lg" wrap="nowrap" className="yy-admin-page-head">
          <div>
            <Title order={2}>部署队列</Title>
            <Text size="sm" className="yy-readable-text">
              查看环境创建、销毁、延期和恢复任务的队列与历史。
            </Text>
          </div>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Select
              placeholder="筛选状态"
              clearable
              data={statusOptions}
              value={statusFilter}
              onChange={(value) => {
                setStatusFilter(value)
                setCursorStack([])
              }}
              w={140}
            />
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={() => mutate()}>
              刷新
            </Button>
          </Group>
        </Group>
        <YinyuTableShell p={0} w="100%" style={{ overflow: 'hidden' }}>
          <Box style={{ overflowX: 'auto' }}>
            <Table miw={1180}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>请求对象</Table.Th>
                  <Table.Th>镜像/模板</Table.Th>
                  <Table.Th>目标节点</Table.Th>
                  <Table.Th>类型</Table.Th>
                  <Table.Th>动作</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>资源</Table.Th>
                  <Table.Th>创建时间</Table.Th>
                  <Table.Th>管理</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={9} style={{ textAlign: 'center' }}>
                      <Text className="yy-readable-text">{isLoading ? '加载中...' : '暂无部署任务'}</Text>
                    </Table.Td>
                  </Table.Tr>
                )}
                {items.map((item) => {
                  const st = statusConfig[item.statusKey] ?? { color: 'gray', semantic: 'unknown' }
                  const canCancel = item.statusKey === 'pending' || item.statusKey === 'assigned' || item.statusKey === 'running'
                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Stack gap={2}>
                          <Text size="sm" fw={700} lineClamp={2}>
                            {item.requestLabel}
                          </Text>
                          <Text size="xs" c="dimmed">
                            {item.ticketId ? `队列 ${item.ticketId.slice(0, 8)}` : `任务 ${item.id.slice(0, 8)}`}
                          </Text>
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" lineClamp={2}>
                          {item.image || '-'}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" lineClamp={2}>
                          {item.targetNodeLabel}
                        </Text>
                      </Table.Td>
                      <Table.Td>{item.typeLabel}</Table.Td>
                      <Table.Td>{item.actionLabel}</Table.Td>
                      <Table.Td>
                        <Tooltip label={item.errorMessage || item.result || ''} disabled={!item.errorMessage && !item.result}>
                          <Badge color={st.color} size="sm" className="yy-semantic-badge" data-semantic={st.semantic}>
                            {item.statusLabel}
                          </Badge>
                        </Tooltip>
                        {item.peopleAhead > 0 && (
                          <Text size="xs" c="dimmed" mt={4}>
                            前方 {item.peopleAhead} 个
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs">{slotsLabel(item)}</Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs">{formatTime(item.createdAt)}</Text>
                      </Table.Td>
                      <Table.Td>
                        {canCancel || item.errorMessage ? (
                          <Group gap={6} wrap="nowrap">
                            {canCancel && (
                              <Tooltip label="取消任务">
                                <ActionIcon color="red" variant="subtle" size="sm" onClick={() => handleCancel(item.id)}>
                                  <Icon path={mdiClose} size={1} />
                                </ActionIcon>
                              </Tooltip>
                            )}
                            {item.errorMessage && (
                              <Tooltip label={item.errorMessage}>
                                <Text size="xs" c="red" span>
                                  错误
                                </Text>
                              </Tooltip>
                            )}
                          </Group>
                        ) : (
                          <Text size="xs" c="dimmed">
                            -
                          </Text>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </Box>
        </YinyuTableShell>
        {(items.length > 0 || page > 1) && (
          <Group justify="space-between" mt="sm" wrap="wrap">
            <Text size="sm" className="yy-readable-text">
              第 {page} 页
            </Text>
            <Group gap="xs">
              <ActionIcon
                aria-label="上一页"
                disabled={cursorStack.length === 0}
                onClick={() => setCursorStack((current) => current.slice(0, -1))}
              >
                <Icon path={mdiArrowLeftBold} size={1} />
              </ActionIcon>
              <ActionIcon
                aria-label="下一页"
                disabled={!nextCursor}
                onClick={() => nextCursor && setCursorStack((current) => [...current, nextCursor])}
              >
                <Icon path={mdiArrowRightBold} size={1} />
              </ActionIcon>
            </Group>
          </Group>
        )}
      </Stack>
    </AdminPage>
  )
}
