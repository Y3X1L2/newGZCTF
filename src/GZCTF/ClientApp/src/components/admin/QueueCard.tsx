import { Group, Text } from '@mantine/core'
import { YinyuPanel, YinyuStatusPill, YinyuStatusState, YinyuStatusTone } from '@Components/yinyu/YinyuUI'

interface QueueItem {
  id: string
  targetNodeId: string
  status: string
  position: number
  createdAt: string
}

function queueTone(status: string): { tone: YinyuStatusTone; state: YinyuStatusState } {
  if (status === 'Queued') return { tone: 'warm', state: 'open' }
  if (status === 'Deploying' || status === 'Running') return { tone: 'success', state: 'running' }
  if (status === 'Failed' || status === 'Error') return { tone: 'danger', state: 'alert' }
  return { tone: 'success', state: 'solved' }
}

export function QueueCard({ item }: { item: QueueItem }) {
  const status = queueTone(item.status)
  return (
    <YinyuPanel p="xs" cells={18}>
      <Group justify="space-between">
        <Text size="sm" ff="monospace">
          {item.id.slice(0, 8)}...
        </Text>
        <YinyuStatusPill tone={status.tone} state={status.state}>
          {item.status}
        </YinyuStatusPill>
        <Text size="xs" c="dimmed">
          位置: {item.position}
        </Text>
      </Group>
    </YinyuPanel>
  )
}
