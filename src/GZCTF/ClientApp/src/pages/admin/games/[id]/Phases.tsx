import { Table, Title, Badge, Button, Group } from '@mantine/core';
import { useParams } from 'react-router';
import { useGamePhases } from '../../../../hooks/useGamePhase';

export default function PhasesPage() {
  const { id } = useParams<{ id: string }>();
  const { phases, isLoading } = useGamePhases(Number(id));

  if (isLoading) return <div>加载中...</div>;
  return (
    <div>
      <Title order={2} mb="lg">比赛阶段管理</Title>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>名称</Table.Th>
            <Table.Th>开始</Table.Th>
            <Table.Th>结束</Table.Th>
            <Table.Th>CTF</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {(phases as any[])?.map((p: any) => (
            <Table.Tr key={p.id}>
              <Table.Td>{p.name}</Table.Td>
              <Table.Td>{new Date(p.startTime).toLocaleString()}</Table.Td>
              <Table.Td>{new Date(p.endTime).toLocaleString()}</Table.Td>
              <Table.Td><Badge color={p.ctfEnabled ? 'green' : 'red'}>{p.ctfEnabled ? '开' : '关'}</Badge></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </div>
  );
}
