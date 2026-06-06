import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router';
import { Card, Title, Text, Badge, Group, Progress, Button, ActionIcon, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { mdiDeleteOutline, mdiArrowLeft, mdiRefresh } from '@mdi/js';
import { Icon } from '@mdi/react';

export default function NodeDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [node, setNode] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  const loadNode = useCallback(async () => {
    try {
      const res = await fetch(`/api/v1/nodes/${id}`);
      if (res.ok) {
        const data = await res.json();
        const { authToken, ...safe } = data;
        setNode(safe);
      }
    } finally { setLoading(false); }
  }, [id]);

  useEffect(() => {
    loadNode();
    const interval = setInterval(loadNode, 15000);
    return () => clearInterval(interval);
  }, [loadNode]);

  const handleDelete = async () => {
    if (!confirm('确定删除此节点？')) return;
    try {
      const res = await fetch(`/api/v1/nodes/${id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '删除成功', message: '节点已移除', color: 'green' });
        navigate('/admin/nodes');
      } else {
        notifications.show({ title: '删除失败', message: '请检查', color: 'red' });
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' });
    }
  };

  if (loading) return <Text>加载中...</Text>;
  if (!node) return <Text>节点不存在</Text>;
  return (
    <div>
      <Group mb="md">
        <Button variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={1} />} onClick={() => navigate('/admin/nodes')}>
          返回节点列表
        </Button>
        <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={loadNode}>刷新</Button>
        <Tooltip label="删除节点">
          <ActionIcon color="red" variant="subtle" onClick={handleDelete}><Icon path={mdiDeleteOutline} size={1} /></ActionIcon>
        </Tooltip>
      </Group>
      <Card shadow="sm" padding="lg" withBorder data-testid={`node-detail-${node.id}`}>
        <Group justify="space-between" mb="md">
          <Title order={2}>{node.name || node.hostAddress}</Title>
          <Badge size="lg" color={node.status === 'Online' ? 'green' : 'red'}>{node.status}</Badge>
        </Group>
        <Text>地址: {node.hostAddress}</Text>
        <Text mt="md">CPU 负载</Text>
        <Progress value={node.cpuLoad * 100} size="lg" color={node.cpuLoad > 0.8 ? 'red' : 'blue'} mb="md" />
        <Text>内存负载</Text>
        <Progress value={node.memoryLoad * 100} size="lg" color={node.memoryLoad > 0.8 ? 'red' : 'blue'} mb="md" />
        <Text>容器: {node.currentContainers}/{node.maxContainers}</Text>
        <Text>VM: {node.currentVms}/{node.maxVms}</Text>
        {node.lastHeartbeat && <Text size="sm" c="dimmed" mt="md">最后心跳: {new Date(node.lastHeartbeat).toLocaleString()}</Text>}
      </Card>
    </div>
  );
}
