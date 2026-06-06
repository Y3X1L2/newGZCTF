import { Card, Badge, Text, Group } from '@mantine/core';

interface QueueItem {
  id: string; targetNodeId: string;
  status: string; position: number;
  createdAt: string;
}

export function QueueCard({ item }: { item: QueueItem }) {
  const statusColor = item.status === 'Queued' ? 'yellow' : item.status === 'Deploying' ? 'blue' : 'green';
  return (
    <Card shadow="sm" padding="xs" withBorder>
      <Group justify="space-between">
        <Text size="sm">{item.id.slice(0, 8)}...</Text>
        <Badge color={statusColor} size="sm">{item.status}</Badge>
        <Text size="xs">位置: {item.position}</Text>
      </Group>
    </Card>
  );
}
