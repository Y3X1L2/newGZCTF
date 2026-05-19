import { useParams } from 'react-router-dom';
import { Card, Title, Text, Badge, Group, Progress } from '@mantine/core';
import { useNodes } from '../../../../hooks/useNodes';

export default function NodeDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { nodes } = useNodes();
  const node = nodes?.find(n => n.id === id);
  if (!node) return <Text>节点不存在</Text>;
  return (
    <Card shadow="sm" padding="lg" withBorder data-testid={`node-detail-${node.id}`}>
      <Group justify="space-between" mb="md">
        <Title order={2}>{node.name}</Title>
        <Badge size="lg" color={node.status === 'Online' ? 'green' : 'red'}>{node.status}</Badge>
      </Group>
      <Text>地址: {node.hostAddress}</Text>
      <Text mt="md">CPU 负载</Text>
      <Progress value={node.cpuLoad * 100} size="lg" color={node.cpuLoad > 0.8 ? 'red' : 'blue'} mb="md" />
      <Text>内存负载</Text>
      <Progress value={node.memoryLoad * 100} size="lg" color={node.memoryLoad > 0.8 ? 'red' : 'blue'} mb="md" />
      <Text>容器: {node.currentContainers}/{node.maxContainers}</Text>
      <Text>VM: {node.currentVms}/{node.maxVms}</Text>
      {node.lastHeartbeat && <Text size="sm" c="dimmed" mt="md">最后心跳: {new Date(node.lastHeartbeat).toLocaleString()}</Text>}
    </Card>
  );
}
