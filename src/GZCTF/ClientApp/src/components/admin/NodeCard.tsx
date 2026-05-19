import { Card, Badge, Text, Group, Progress } from '@mantine/core';
import type { NodeInfo } from '../../hooks/useNodes';

export function NodeCard({ node }: { node: NodeInfo }) {
  const statusColor = node.status === 'Online' ? 'green' : node.status === 'Offline' ? 'red' : 'yellow';
  return (
    <Card shadow="sm" padding="md" withBorder data-testid={`node-card-${node.id}`}>
      <Group justify="space-between" mb="xs">
        <Text fw={700}>{node.name}</Text>
        <Badge color={statusColor}>{node.status}</Badge>
      </Group>
      <Text size="sm" c="dimmed">{node.hostAddress}</Text>
      <Text size="xs" mt="xs">CPU: {(node.cpuLoad * 100).toFixed(0)}%</Text>
      <Progress value={node.cpuLoad * 100} color={node.cpuLoad > 0.8 ? 'red' : 'blue'} size="sm" mb="xs" />
      <Text size="xs">容器: {node.currentContainers}/{node.maxContainers}</Text>
      <Text size="xs">VM: {node.currentVms}/{node.maxVms}</Text>
    </Card>
  );
}
