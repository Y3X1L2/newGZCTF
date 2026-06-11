import { Button, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { CleanupButton } from '@Components/admin/CleanupButton'
import { DeployButton } from '@Components/admin/DeployButton'
import { NodeCard } from '@Components/admin/NodeCard'
import { YinyuPanel, YinyuStatePage } from '@Components/yinyu/YinyuUI'

export default function DashboardPage() {
  const [nodes, setNodes] = useState<any[]>([])
  const [isLoading, setIsLoading] = useState(true)

  const loadNodes = useCallback(async () => {
    try {
      const res = await fetch('/api/v1/nodes')
      if (res.ok) setNodes(await res.json())
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    loadNodes()
    const interval = setInterval(loadNodes, 15000)
    return () => clearInterval(interval)
  }, [loadNodes])

  return (
    <AdminPage isLoading={isLoading && !nodes.length}>
      <Stack data-testid="admin-dashboard" gap="lg" w="100%">
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>部署管理仪表盘</Title>
            <Text size="sm" c="dimmed">
              集中查看节点状态，并执行一键部署与资源清理。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={loadNodes}>
              刷新
            </Button>
            <DeployButton onDeployed={loadNodes} />
            <CleanupButton onCleanup={loadNodes} />
          </Group>
        </Group>

        <YinyuPanel p="md">
          <Group justify="space-between" mb="md">
            <Title order={4}>节点状态</Title>
            <Text size="sm" c="dimmed">
              {nodes?.length ?? 0} 个节点
            </Text>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
            {nodes?.map((node) => (
              <NodeCard key={node.id} node={node} />
            ))}
          </SimpleGrid>
          {nodes?.length === 0 && !isLoading && (
            <YinyuStatePage mt="lg" p="lg">
              <Text fw={700} ta="center">
                暂无节点
              </Text>
              <Text c="dimmed" size="sm" ta="center">
                通过一键部署添加新的分布式节点。
              </Text>
            </YinyuStatePage>
          )}
        </YinyuPanel>
      </Stack>
    </AdminPage>
  )
}
