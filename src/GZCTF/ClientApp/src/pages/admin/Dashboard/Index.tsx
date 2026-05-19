import { Group, Title, SimpleGrid } from '@mantine/core';
import { DeployButton } from '../../../components/admin/DeployButton';
import { CleanupButton } from '../../../components/admin/CleanupButton';
import { NodeCard } from '../../../components/admin/NodeCard';
import { useNodes } from '../../../hooks/useNodes';

export default function DashboardPage() {
  const { nodes, isLoading } = useNodes();

  return (
    <div data-testid="admin-dashboard">
      <Group justify="space-between" mb="lg">
        <Title order={2}>部署管理仪表盘</Title>
        <Group>
          <DeployButton />
          <CleanupButton />
        </Group>
      </Group>
      <Title order={4} mb="md">节点状态 ({nodes?.length ?? 0})</Title>
      <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
        {nodes?.map(node => <NodeCard key={node.id} node={node} />)}
      </SimpleGrid>
    </div>
  );
}
