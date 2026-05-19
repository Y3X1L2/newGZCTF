import { SimpleGrid, Title, Text } from '@mantine/core';
import { NodeCard } from '../../../components/admin/NodeCard';
import { useNodes } from '../../../hooks/useNodes';

export default function NodesPage() {
  const { nodes, isLoading } = useNodes();
  if (isLoading) return <Text>加载中...</Text>;
  return (
    <div data-testid="nodes-page">
      <Title order={2} mb="lg">节点管理 ({nodes?.length ?? 0})</Title>
      <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
        {nodes?.map(node => <NodeCard key={node.id} node={node} />)}
      </SimpleGrid>
    </div>
  );
}
