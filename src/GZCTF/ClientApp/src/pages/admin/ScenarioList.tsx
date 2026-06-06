import { useState, useEffect } from 'react';
import { Table, Button, Group, Badge, TextInput, Modal, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';

interface ScenarioSummary {
  id: number;
  title: string;
  gameId: number;
  gameTitle: string;
  stageCount: number;
  status: string;
  isEnabled: boolean;
  createdAt: string;
}

export default function ScenarioList() {
  const navigate = useNavigate();
  const [scenarios, setScenarios] = useState<ScenarioSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<ScenarioSummary | null>(null);
  const [deleting, setDeleting] = useState(false);

  const loadScenarios = async () => {
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/scenarios?search=${encodeURIComponent(search)}`);
      if (res.ok) {
        const data = await res.json();
        setScenarios(data.items ?? data);
      }
    } finally { setLoading(false); }
  };

  useEffect(() => { loadScenarios(); }, [search]);

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      const res = await fetch(`/api/v1/scenarios/${deleteTarget.id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '已删除', message: `场景 "${deleteTarget.title}" 已删除`, color: 'green' });
        setScenarios(scenarios.filter(s => s.id !== deleteTarget.id));
        setDeleteTarget(null);
      } else throw new Error('Delete failed');
    } catch {
      notifications.show({ title: '删除失败', message: '请确保场景处于 Draft 状态', color: 'red' });
    } finally { setDeleting(false); }
  };

  if (loading) return <Text>加载中...</Text>;

  return (
    <div style={{ maxWidth: 1000, margin: '0 auto', padding: '1rem' }}>
      <Group justify="space-between" mb="md">
        <h2>场景管理</h2>
        <Button onClick={() => navigate('/admin/scenarios/new')}>创建新场景</Button>
      </Group>

      <TextInput placeholder="搜索场景..." value={search} onChange={e => setSearch(e.currentTarget.value)} mb="md" />

      <Table striped highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>ID</Table.Th>
            <Table.Th>标题</Table.Th>
            <Table.Th>赛事</Table.Th>
            <Table.Th>阶段数</Table.Th>
            <Table.Th>状态</Table.Th>
            <Table.Th>创建时间</Table.Th>
            <Table.Th>操作</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {scenarios.map(s => (
            <Table.Tr key={s.id}>
              <Table.Td>{s.id}</Table.Td>
              <Table.Td>{s.title}</Table.Td>
              <Table.Td>{s.gameTitle ?? `赛事 #${s.gameId}`}</Table.Td>
              <Table.Td>{s.stageCount}</Table.Td>
              <Table.Td>
                <Badge color={s.isEnabled ? 'green' : 'yellow'}>{s.isEnabled ? '已发布' : s.status}</Badge>
              </Table.Td>
              <Table.Td>{new Date(s.createdAt).toLocaleDateString('zh-CN')}</Table.Td>
              <Table.Td>
                <Group gap="xs">
                  <Button size="xs" onClick={() => navigate(`/admin/scenarios/${s.id}/edit`)}>编辑</Button>
                  {!s.isEnabled && (
                    <Button size="xs" color="red" onClick={() => setDeleteTarget(s)}>删除</Button>
                  )}
                </Group>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>

      <Modal opened={!!deleteTarget} onClose={() => setDeleteTarget(null)} title="确认删除">
        <Text>确定要删除场景 "{deleteTarget?.title}" 吗？此操作不可撤销。</Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setDeleteTarget(null)}>取消</Button>
          <Button color="red" loading={deleting} onClick={handleDelete}>确认删除</Button>
        </Group>
      </Modal>
    </div>
  );
}
