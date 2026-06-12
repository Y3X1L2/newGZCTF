import { Badge, Stack, Table, Text, Title } from '@mantine/core'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { useGamePhases } from '../../../../hooks/useGamePhase'

export default function PhasesPage() {
  const { id } = useParams<{ id: string }>()
  const { phases, isLoading } = useGamePhases(Number(id))

  return (
    <WithGameEditTab isLoading={isLoading}>
      <Stack gap="md">
        <Stack gap={2}>
          <Title order={2}>比赛阶段管理</Title>
          <Text size="sm" c="dimmed">
            查看赛事阶段时间与 CTF 功能启停状态。
          </Text>
        </Stack>
        <YinyuTableShell p="xs">
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>名称</Table.Th>
                <Table.Th>开始</Table.Th>
                <Table.Th>结束</Table.Th>
                <Table.Th>CTF</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(phases as any[])?.map((phase: any) => (
                <Table.Tr key={phase.id}>
                  <Table.Td>{phase.name}</Table.Td>
                  <Table.Td>{new Date(phase.startTime).toLocaleString()}</Table.Td>
                  <Table.Td>{new Date(phase.endTime).toLocaleString()}</Table.Td>
                  <Table.Td>
                    <Badge color={phase.ctfEnabled ? 'green' : 'red'} className="yy-status-badge">
                      {phase.ctfEnabled ? '开' : '关'}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </YinyuTableShell>
      </Stack>
    </WithGameEditTab>
  )
}
