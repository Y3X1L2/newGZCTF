import { Table, Title } from '@mantine/core';

export default function QueuePage() {
  return (
    <div data-testid="queue-page">
      <Title order={2} mb="lg">部署队列</Title>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>请求ID</Table.Th>
            <Table.Th>目标节点</Table.Th>
            <Table.Th>类型</Table.Th>
            <Table.Th>状态</Table.Th>
            <Table.Th>创建时间</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          <Table.Tr>
            <Table.Td colSpan={5} style={{ textAlign: 'center' }}>暂无排队请求</Table.Td>
          </Table.Tr>
        </Table.Tbody>
      </Table>
    </div>
  );
}
