import { Table, Title, Text, Badge, Group, Button, Select, ActionIcon, Tooltip, Stack, Box } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiRefresh, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useState } from 'react'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'

const fetcher = (url: string) => fetch(url).then((r) => r.json())

const typeLabels: Record<number, string> = { 0: 'Docker', 1: 'VM' }
const actionLabels: Record<number, string> = { 0: '创建', 1: '启动', 2: '销毁', 3: '快照恢复' }
const statusConfig: Record<number, { label: string; color: string; semantic: string }> = {
  0: { label: '等待中', color: 'violet', semantic: 'pending' },
  1: { label: '执行中', color: 'blue', semantic: 'running' },
  2: { label: '已完成', color: 'green', semantic: 'success' },
  3: { label: '失败', color: 'red', semantic: 'failed' },
  4: { label: '已取消', color: 'gray', semantic: 'canceled' },
}

export default function QueuePage() {
  const [statusFilter, setStatusFilter] = useState<string | null>(null)
  const { data, isLoading, mutate } = useSWR(
    `/api/v1/deployment-targets${statusFilter ? `?status=${statusFilter}` : ''}`,
    fetcher,
    { refreshInterval: 10000 }
  )

  const handleCancel = async (id: string) => {
    try {
      const res = await fetch(`/api/v1/deployment-targets/${id}`, { method: 'DELETE' })
      if (res.ok) {
        notifications.show({ title: '已取消', message: '部署任务已取消', color: 'green' })
        mutate()
      } else {
        notifications.show({ title: '取消失败', message: '请检查', color: 'red' })
      }
    } catch {
      notifications.show({ title: '取消失败', message: '网络错误', color: 'red' })
    }
  }

  const items = (data?.items as any[]) ?? []

  return (
    <AdminPage>
      <Stack data-testid="queue-page" gap="lg" w="100%">
        <Group justify="space-between" mb="lg" wrap="nowrap" className="yy-admin-page-head">
          <div>
            <Title order={2}>部署队列</Title>
            <Text size="sm" className="yy-readable-text">
              统一查看环境创建、启动、销毁与快照恢复任务。
            </Text>
          </div>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Select
              placeholder="筛选状态"
              clearable
              data={[
                { value: '0', label: '等待中' },
                { value: '1', label: '执行中' },
                { value: '2', label: '已完成' },
                { value: '3', label: '失败' },
              ]}
              value={statusFilter}
              onChange={setStatusFilter}
              w={140}
            />
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={() => mutate()}>
              刷新
            </Button>
          </Group>
        </Group>
        <YinyuTableShell p={0} w="100%" style={{ overflow: 'hidden' }}>
          <Box style={{ overflowX: 'auto' }}>
            <Table miw={960}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>请求ID</Table.Th>
                  <Table.Th>目标节点</Table.Th>
                  <Table.Th>类型</Table.Th>
                  <Table.Th>操作</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>创建时间</Table.Th>
                  <Table.Th>操作</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={7} style={{ textAlign: 'center' }}>
                      <Text className="yy-readable-text">{isLoading ? '加载中...' : '暂无部署请求'}</Text>
                    </Table.Td>
                  </Table.Tr>
                )}
                {items.map((item: any) => {
                  const st = statusConfig[item.status] ?? { label: 'Unknown', color: 'gray', semantic: 'unknown' }
                  const canCancel = item.status === 0 || item.status === 1
                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Text size="xs" ff="monospace">
                          {item.id?.substring(0, 8)}...
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" ff="monospace">
                          {item.targetNodeId === '00000000-0000-0000-0000-000000000000'
                            ? '未分配'
                            : item.targetNodeId?.substring(0, 8) + '...'}
                        </Text>
                      </Table.Td>
                      <Table.Td>{typeLabels[item.type] ?? item.type}</Table.Td>
                      <Table.Td>{actionLabels[item.action] ?? item.action}</Table.Td>
                      <Table.Td>
                        <Badge color={st.color} size="sm" className="yy-semantic-badge" data-semantic={st.semantic}>
                          {st.label}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs">{new Date(item.createdAt).toLocaleString()}</Text>
                      </Table.Td>
                      <Table.Td>
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
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </Box>
        </YinyuTableShell>
        {data?.total > 0 && (
          <Text size="sm" className="yy-readable-text" mt="sm">
            共 {data.total} 条记录
          </Text>
        )}
      </Stack>
    </AdminPage>
  )
}
