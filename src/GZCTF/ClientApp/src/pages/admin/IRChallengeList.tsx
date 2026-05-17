import { useState, useEffect } from 'react';
import { Table, Button, Group, Badge, TextInput, Modal, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';

interface IRChallengeSummary {
  id: number;
  title: string;
  gameId: number;
  gameTitle: string;
  osType: string;
  checkpointCount: number;
  status: string;
  isEnabled: boolean;
  createdAt: string;
}

export default function IRChallengeList() {
  const navigate = useNavigate();
  const [challenges, setChallenges] = useState<IRChallengeSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<IRChallengeSummary | null>(null);
  const [deleting, setDeleting] = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/ir-challenges?search=${encodeURIComponent(search)}`);
      if (res.ok) {
        const data = await res.json();
        setChallenges(data.items ?? data);
      }
    } finally { setLoading(false); }
  };

  useEffect(() => { load(); }, [search]);

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await fetch(`/api/v1/ir-challenges/${deleteTarget.id}`, { method: 'DELETE' });
      notifications.show({ title: '已删除', color: 'green' });
      setChallenges(challenges.filter(c => c.id !== deleteTarget.id));
      setDeleteTarget(null);
    } catch {
      notifications.show({ title: '删除失败', color: 'red' });
    } finally { setDeleting(false); }
  };

  if (loading) return <Text>加载中...</Text>;

  return (
    <div style={{ maxWidth: 1000, margin: '0 auto', padding: '1rem' }}>
      <Group justify="space-between" mb="md">
        <h2>应急响应题目管理</h2>
        <Button onClick={() => navigate('/admin/ir-challenges/new')}>创建新题目</Button>
      </Group>

      <TextInput placeholder="搜索IR题目..." value={search} onChange={e => setSearch(e.currentTarget.value)} mb="md" />

      <Table striped highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>ID</Table.Th>
            <Table.Th>标题</Table.Th>
            <Table.Th>赛事</Table.Th>
            <Table.Th>系统</Table.Th>
            <Table.Th>检查点</Table.Th>
            <Table.Th>状态</Table.Th>
            <Table.Th>创建时间</Table.Th>
            <Table.Th>操作</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {challenges.map(c => (
            <Table.Tr key={c.id}>
              <Table.Td>{c.id}</Table.Td>
              <Table.Td>{c.title}</Table.Td>
              <Table.Td>{c.gameTitle ?? `赛事 #${c.gameId}`}</Table.Td>
              <Table.Td><Badge color={c.osType === 'Windows' ? 'blue' : 'green'}>{c.osType}</Badge></Table.Td>
              <Table.Td>{c.checkpointCount}</Table.Td>
              <Table.Td>
                <Badge color={c.isEnabled ? 'green' : 'yellow'}>{c.isEnabled ? '已发布' : '草稿'}</Badge>
              </Table.Td>
              <Table.Td>{new Date(c.createdAt).toLocaleDateString('zh-CN')}</Table.Td>
              <Table.Td>
                <Group gap="xs">
                  <Button size="xs" onClick={() => navigate(`/admin/ir-challenges/${c.id}/edit`)}>编辑</Button>
                  <Button size="xs" color="red" onClick={() => setDeleteTarget(c)}>删除</Button>
                </Group>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>

      <Modal opened={!!deleteTarget} onClose={() => setDeleteTarget(null)} title="确认删除">
        <Text>确定要删除 "{deleteTarget?.title}" 吗？</Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setDeleteTarget(null)}>取消</Button>
          <Button color="red" loading={deleting} onClick={handleDelete}>确认删除</Button>
        </Group>
      </Modal>
    </div>
  );
}
