import { Table, Title, Text, Badge } from '@mantine/core';
import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

export default function ImagesPage() {
  const { data, isLoading } = useSWR('/api/v1/image-templates', fetcher);
  if (isLoading) return <Text>加载中...</Text>;
  return (
    <div>
      <Title order={2} mb="lg">镜像模板管理</Title>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>名称</Table.Th>
            <Table.Th>类型</Table.Th>
            <Table.Th>系统</Table.Th>
            <Table.Th>大小</Table.Th>
            <Table.Th>状态</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {(data?.items as any[])?.map((img: any) => (
            <Table.Tr key={img.id}>
              <Table.Td>{img.name}</Table.Td>
              <Table.Td>{img.imageType}</Table.Td>
              <Table.Td><Badge>{img.osType === 0 ? 'Linux' : 'Windows'}</Badge></Table.Td>
              <Table.Td>{(img.fileSize / 1024 / 1024).toFixed(1)} MB</Table.Td>
              <Table.Td><Badge color={img.status === 0 ? 'green' : 'yellow'}>{img.status === 0 ? 'Ready' : 'Importing'}</Badge></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </div>
  );
}
