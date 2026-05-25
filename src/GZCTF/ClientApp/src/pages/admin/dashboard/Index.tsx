import { useEffect, useState, useCallback } from 'react';
import { Group, Title, SimpleGrid, Text, Button } from '@mantine/core';
import { DeployButton } from '../../../components/admin/DeployButton';
import { CleanupButton } from '../../../components/admin/CleanupButton';
import { NodeCard } from '../../../components/admin/NodeCard';
import { mdiRefresh } from '@mdi/js';
import { Icon } from '@mdi/react';

export default function DashboardPage() {
  const [nodes, setNodes] = useState<any[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const loadNodes = useCallback(async () => {
    try {
      const res = await fetch('/api/v1/nodes');
      if (res.ok) setNodes(await res.json());
    } finally { setIsLoading(false); }
  }, []);

  useEffect(() => {
    loadNodes();
    const interval = setInterval(loadNodes, 15000);
    return () => clearInterval(interval);
  }, [loadNodes]);

  return (
    <div data-testid="admin-dashboard">
      <Group justify="space-between" mb="lg">
        <Title order={2}>部署管理仪表盘</Title>
        <Group>
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={loadNodes}>刷新</Button>
          <DeployButton onDeployed={loadNodes} />
          <CleanupButton onCleanup={loadNodes} />
        </Group>
      </Group>
      <Title order={4} mb="md">节点状态 ({nodes?.length ?? 0})</Title>
      <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
        {nodes?.map(node => <NodeCard key={node.id} node={node} />)}
      </SimpleGrid>
      {nodes?.length === 0 && !isLoading && <Text c="dimmed" ta="center" mt="lg">暂无节点</Text>}
    </div>
  );
}
