import { ActionIcon, Badge, Button, Group, Progress, SimpleGrid, Stack, Text, Title, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiArrowLeft, mdiDeleteOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuMetricTile, YinyuPanel, YinyuStatePage } from '@Components/yinyu/YinyuUI'

export default function NodeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [node, setNode] = useState<any>(null)
  const [loading, setLoading] = useState(true)

  const loadNode = useCallback(async () => {
    try {
      const res = await fetch(`/api/v1/nodes/${id}`)
      if (res.ok) {
        const data = await res.json()
        const { authToken, ...safe } = data
        setNode(safe)
      }
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    loadNode()
    const interval = setInterval(loadNode, 15000)
    return () => clearInterval(interval)
  }, [loadNode])

  const handleDelete = async () => {
    if (!confirm('确定删除此节点？')) return

    try {
      const res = await fetch(`/api/v1/nodes/${id}`, { method: 'DELETE' })
      if (res.ok) {
        notifications.show({ title: '删除成功', message: '节点已移除', color: 'green' })
        navigate('/admin/nodes')
      } else {
        notifications.show({ title: '删除失败', message: '请检查节点状态', color: 'red' })
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' })
    }
  }

  if (loading) return <AdminPage isLoading />
  if (!node) {
    return (
      <AdminPage>
        <YinyuStatePage p="lg">
          <Text>节点不存在</Text>
        </YinyuStatePage>
      </AdminPage>
    )
  }

  return (
    <AdminPage>
      <Stack gap="lg" w="100%">
        <Group>
          <Button
            variant="subtle"
            leftSection={<Icon path={mdiArrowLeft} size={1} />}
            onClick={() => navigate('/admin/nodes')}
          >
            返回节点列表
          </Button>
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={loadNode}>
            刷新
          </Button>
          <Tooltip label="删除节点">
            <ActionIcon color="red" variant="subtle" onClick={handleDelete}>
              <Icon path={mdiDeleteOutline} size={1} />
            </ActionIcon>
          </Tooltip>
        </Group>

        <YinyuPanel p="lg" data-testid={`node-detail-${node.id}`}>
          <Group justify="space-between" mb="md">
            <Stack gap={2}>
              <Title order={2}>{node.name || node.hostAddress}</Title>
              <Text c="dimmed" size="sm">
                地址: {node.hostAddress}
              </Text>
            </Stack>
            <Badge
              size="lg"
              color={node.status === 'Online' ? 'green' : 'red'}
              className="yy-status-badge yy-semantic-badge"
              data-semantic={node.status === 'Online' ? 'online' : 'offline'}
            >
              {node.status}
            </Badge>
          </Group>

          <Stack gap="md">
            <Stack gap={6}>
              <Group justify="space-between">
                <Text size="sm" fw={700}>
                  CPU 负载
                </Text>
                <Text size="sm" c="dimmed">
                  {Math.round((node.cpuLoad ?? 0) * 100)}%
                </Text>
              </Group>
              <Progress value={(node.cpuLoad ?? 0) * 100} size="lg" color={node.cpuLoad > 0.8 ? 'red' : 'blue'} />
            </Stack>

            <Stack gap={6}>
              <Group justify="space-between">
                <Text size="sm" fw={700}>
                  内存负载
                </Text>
                <Text size="sm" c="dimmed">
                  {Math.round((node.memoryLoad ?? 0) * 100)}%
                </Text>
              </Group>
              <Progress value={(node.memoryLoad ?? 0) * 100} size="lg" color={node.memoryLoad > 0.8 ? 'red' : 'blue'} />
            </Stack>

            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <YinyuMetricTile label="容器" value={`${node.currentContainers}/${node.maxContainers}`} tone="neutral" />
              <YinyuMetricTile label="VM" value={`${node.currentVms}/${node.maxVms}`} tone="neutral" />
            </SimpleGrid>
          </Stack>

          {node.lastHeartbeat && (
            <Text size="sm" c="dimmed" mt="md">
              最后心跳 {new Date(node.lastHeartbeat).toLocaleString()}
            </Text>
          )}
        </YinyuPanel>
      </Stack>
    </AdminPage>
  )
}
