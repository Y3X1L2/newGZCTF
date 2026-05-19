import { Table, Button, Group, Title, Badge, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

interface DockerImageItem {
  id: number; name: string; imageTag?: string;
  osType: number; status: number; fileSize: number;
  createdAt: string;
}

export default function DockerImagesPage() {
  const { data, error, isLoading, mutate } = useSWR<DockerImageItem[]>('/api/v1/docker/images', fetcher);

  const handleDelete = async (id: number) => {
    await fetch(`/api/v1/docker/images/${id}`, { method: 'DELETE' });
    mutate();
    notifications.show({ title: '已删除', message: 'Docker 镜像已移除', color: 'green' });
  };

  if (isLoading) return <Text>加载中...</Text>;

  return (
    <div data-testid="docker-images-page">
      <Group justify="space-between" mb="lg">
        <Title order={2}>Docker 镜像管理</Title>
      </Group>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>名称</Table.Th>
            <Table.Th>标签</Table.Th>
            <Table.Th>系统</Table.Th>
            <Table.Th>大小</Table.Th>
            <Table.Th>操作</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {data?.map(img => (
            <Table.Tr key={img.id}>
              <Table.Td>{img.name}</Table.Td>
              <Table.Td>{img.imageTag || '-'}</Table.Td>
              <Table.Td><Badge>{img.osType === 0 ? 'Linux' : 'Windows'}</Badge></Table.Td>
              <Table.Td>{(img.fileSize / 1024 / 1024).toFixed(1)} MB</Table.Td>
              <Table.Td>
                <Button size="xs" color="red" onClick={() => handleDelete(img.id)}>删除</Button>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </div>
  );
}
