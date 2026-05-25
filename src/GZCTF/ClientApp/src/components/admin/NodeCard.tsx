import { Card, Badge, Text, Group, Progress, Switch } from '@mantine/core';

interface NodeInfo {
  id: string; name: string; hostAddress: string; status: string;
  cpuLoad: number; memoryLoad: number; currentContainers: number; maxContainers: number;
  currentVms: number; maxVms: number; lastHeartbeat: string;
  isSchedulable: boolean; isLocal: boolean; agentPort: number;
}

export function NodeCard({ node, onToggleSchedulable }: { node: NodeInfo; onToggleSchedulable?: (id: string, val: boolean) => void }) {
  const statusColor = node.status === 'Online' ? 'green' : node.status === 'Offline' ? 'red' : 'yellow';
  return (
    <Card shadow="sm" padding="md" withBorder data-testid={`node-card-${node.id}`}>
      <Group justify="space-between" mb="xs">
        <Group gap="xs">
          <Text fw={700}>{node.name}</Text>
          {node.isLocal && <Badge size="sm" variant="light" color="blue">本地</Badge>}
        </Group>
        <Badge color={statusColor}>{node.status}</Badge>
      </Group>
      <Text size="sm" c="dimmed">{node.hostAddress}</Text>
      <Text size="xs" mt="xs">CPU: {(node.cpuLoad * 100).toFixed(0)}%</Text>
      <Progress value={node.cpuLoad * 100} color={node.cpuLoad > 0.8 ? 'red' : 'blue'} size="sm" mb="xs" />
      <Text size="xs">容器: {node.currentContainers}/{node.maxContainers}</Text>
      <Text size="xs">VM: {node.currentVms}/{node.maxVms}</Text>
      <Text size="xs" c="dimmed">Agent 端口: {node.agentPort}</Text>
      <Switch
        label="参与调度"
        checked={node.isSchedulable}
        onChange={(e) => onToggleSchedulable?.(node.id, e.currentTarget.checked)}
        mt="xs"
      />
    </Card>
  );
}
