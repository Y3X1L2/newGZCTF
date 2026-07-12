import { Badge, Button, Group, Stack, Table, Text } from '@mantine/core'
import { mdiDeleteOutline, mdiRefresh, mdiRestart } from '@mdi/js'
import { Icon } from '@mdi/react'
import { memo } from 'react'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { PenetrationRuntimeBindingModel } from '@Api/PenetrationApi'
import { TeamLabRuntimeStatus } from '@Api/TeamLabApi'

interface Props {
  runtimes: PenetrationRuntimeBindingModel[]
  busy: boolean
  onRefresh: () => void
  onRebuild: (teamId: number) => void
  onCleanup: (teamId: number) => void
}

const statusColor: Record<TeamLabRuntimeStatus, string> = {
  [TeamLabRuntimeStatus.Pending]: 'gray',
  [TeamLabRuntimeStatus.Planning]: 'yellow',
  [TeamLabRuntimeStatus.Scheduled]: 'yellow',
  [TeamLabRuntimeStatus.Deploying]: 'blue',
  [TeamLabRuntimeStatus.Probing]: 'cyan',
  [TeamLabRuntimeStatus.Running]: 'teal',
  [TeamLabRuntimeStatus.Failed]: 'red',
  [TeamLabRuntimeStatus.CleanupPending]: 'orange',
  [TeamLabRuntimeStatus.Stopped]: 'gray',
  [TeamLabRuntimeStatus.Destroying]: 'orange',
  [TeamLabRuntimeStatus.Destroyed]: 'gray',
}

export const TeamLabRuntimeObservability = memo(({ runtimes, busy, onRefresh, onRebuild, onCleanup }: Props) => (
  <YinyuTableShell p="xs">
    <Group justify="space-between" mb="xs">
      <Text fw={800}>队伍运行环境</Text>
      <Button size="compact-sm" variant="light" leftSection={<Icon path={mdiRefresh} size={0.7} />} disabled={busy} onClick={onRefresh}>
        刷新
      </Button>
    </Group>
    <Table.ScrollContainer minWidth={760}>
      <Table>
        <Table.Thead>
          <Table.Tr><Table.Th>队伍</Table.Th><Table.Th>状态</Table.Th><Table.Th>代次</Table.Th><Table.Th>分片 / 资产</Table.Th><Table.Th>运行标识</Table.Th><Table.Th>操作</Table.Th></Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {runtimes.length ? runtimes.map((runtime) => (
            <Table.Tr key={runtime.runtimeId}>
              <Table.Td>{runtime.teamName}</Table.Td>
              <Table.Td>
                <Stack gap={2}>
                  <Badge color={statusColor[runtime.status]} variant="light">{runtime.status}</Badge>
                  {runtime.error ? <Text size="xs" c="red">{runtime.error}</Text> : null}
                </Stack>
              </Table.Td>
              <Table.Td>{runtime.generation}</Table.Td>
              <Table.Td>{runtime.shardCount} / {runtime.assetCount}</Table.Td>
              <Table.Td><Text size="xs" ff="monospace">{runtime.runtimeId}</Text></Table.Td>
              <Table.Td>
                <Group gap="xs" wrap="nowrap">
                  <Button size="compact-xs" variant="light" leftSection={<Icon path={mdiRestart} size={0.65} />} disabled={busy} onClick={() => onRebuild(runtime.teamId)}>重置</Button>
                  <Button size="compact-xs" color="red" variant="light" leftSection={<Icon path={mdiDeleteOutline} size={0.65} />} disabled={busy} onClick={() => onCleanup(runtime.teamId)}>销毁</Button>
                </Group>
              </Table.Td>
            </Table.Tr>
          )) : <Table.Tr><Table.Td colSpan={6}><Text size="sm" c="dimmed">暂无运行环境</Text></Table.Td></Table.Tr>}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  </YinyuTableShell>
))

TeamLabRuntimeObservability.displayName = 'TeamLabRuntimeObservability'
