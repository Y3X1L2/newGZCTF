import { useState } from 'react';
import { Table, Title, Text, Badge, Group, Button, Select, ActionIcon, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { mdiRefresh, mdiClose } from '@mdi/js';
import { Icon } from '@mdi/react';
import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

const typeLabels: Record<number, string> = { 0: 'Docker', 1: 'VM' };
const actionLabels: Record<number, string> = { 0: '创建', 1: '启动', 2: '销毁', 3: '快照恢复' };
const statusConfig: Record<number, { label: string; color: string }> = {
  0: { label: '等待中', color: 'yellow' },
  1: { label: '执行中', color: 'blue' },
  2: { label: '已完成', color: 'green' },
  3: { label: '失败', color: 'red' },
  4: { label: '已取消', color: 'gray' },
};

export default function QueuePage() {
  const [statusFilter, setStatusFilter] = useState<string | null>(null);
  const { data, isLoading, mutate } = useSWR(
    `/api/v1/deployment-targets${statusFilter ? `?status=${statusFilter}` : ''}`,
    fetcher,
    { refreshInterval: 10000 }
  );

  const handleCancel = async (id: string) => {
    try {
      const res = await fetch(`/api/v1/deployment-targets/${id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '已取消', message: '部署任务已取消', color: 'green' });
        mutate();
      } else {
        notifications.show({ title: '取消失败', message: '请检查', color: 'red' });
      }
    } catch {
      notifications.show({ title: '取消失败', message: '网络错误', color: 'red' });
    }
  };

  const items = (data?.items as any[]) ?? [];

  return (
    <div data-testid="queue-page">
      <Group justify="space-between" mb="lg">
        <Title order={2}>部署队列</Title>
        <Group>
          <Select
            placeholder="筛选状态"
            clearable
            data={[
              { value: '0', label: '等待中' },
              { value: '1', label: '执行中' },
              { value: '2', label: '已完成' },
              { value: '3', label: '失败' },
            ]}
            value={statusFilter}
            onChange={setStatusFilter}
            w={140}
          />
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={() => mutate()}>刷新</Button>
        </Group>
      </Group>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>请求ID</Table.Th>
            <Table.Th>目标节点</Table.Th>
            <Table.Th>类型</Table.Th>
            <Table.Th>操作</Table.Th>
            <Table.Th>状态</Table.Th>
            <Table.Th>创建时间</Table.Th>
            <Table.Th>操作</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {items.length === 0 && (
            <Table.Tr>
              <Table.Td colSpan={7} style={{ textAlign: 'center' }}>
                <Text c="dimmed">{isLoading ? '加载中...' : '暂无部署请求'}</Text>
              </Table.Td>
            </Table.Tr>
          )}
          {items.map((item: any) => {
            const st = statusConfig[item.status] ?? { label: 'Unknown', color: 'gray' };
            const canCancel = item.status === 0 || item.status === 1;
            return (
              <Table.Tr key={item.id}>
                <Table.Td><Text size="xs" ff="monospace">{item.id?.substring(0, 8)}...</Text></Table.Td>
                <Table.Td><Text size="xs" ff="monospace">{item.targetNodeId === '00000000-0000-0000-0000-000000000000' ? '未分配' : item.targetNodeId?.substring(0, 8) + '...'}</Text></Table.Td>
                <Table.Td>{typeLabels[item.type] ?? item.type}</Table.Td>
                <Table.Td>{actionLabels[item.action] ?? item.action}</Table.Td>
                <Table.Td><Badge color={st.color} size="sm">{st.label}</Badge></Table.Td>
                <Table.Td><Text size="xs">{new Date(item.createdAt).toLocaleString()}</Text></Table.Td>
                <Table.Td>
                  {canCancel && (
                    <Tooltip label="取消任务">
                      <ActionIcon color="red" variant="subtle" size="sm" onClick={() => handleCancel(item.id)}>
                        <Icon path={mdiClose} size={1} />
                      </ActionIcon>
                    </Tooltip>
                  )}
                  {item.errorMessage && (
                    <Tooltip label={item.errorMessage}>
                      <Text size="xs" c="red" span>错误</Text>
                    </Tooltip>
                  )}
                </Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
      {data?.total > 0 && (
        <Text size="sm" c="dimmed" mt="sm">共 {data.total} 条记录</Text>
      )}
    </div>
  );
}
