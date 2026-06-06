import { useState, useEffect, useCallback } from 'react';
import { SimpleGrid, Title, Text, Button, Modal, TextInput, Group, ActionIcon, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { mdiDeleteOutline, mdiRefresh } from '@mdi/js';
import { Icon } from '@mdi/react';
import { NodeCard } from '../../../components/admin/NodeCard';
import { CleanupButton } from '../../../components/admin/CleanupButton';

function AddNodeModal({ opened, onClose, onAdded }: { opened: boolean; onClose: () => void; onAdded: () => void }) {
  const [host, setHost] = useState('');
  const [user, setUser] = useState('root');
  const [pass, setPass] = useState('');
  const [name, setName] = useState('');
  const [loading, setLoading] = useState(false);

  const handleAdd = async () => {
    setLoading(true);
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ hostAddress: host, username: user, password: pass, nodeName: name })
      });
      const data = await res.json();
      if (res.ok) {
        notifications.show({
          title: 'Deployment succeeded',
          message: `Node ${data.nodeName || host} connected`,
          color: 'green'
        });
        onAdded();
        onClose();
        setHost(''); setUser('root'); setPass(''); setName('');
      } else {
        notifications.show({
          title: 'Deployment failed',
          message: data.message || data,
          color: 'red'
        });
      }
    } catch {
      notifications.show({
        title: 'Connection failed',
        message: 'Unable to connect to platform API',
        color: 'red'
      });
    } finally { setLoading(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="Add Target Server" data-testid="add-node-modal">
      <TextInput label="Server Name" value={name} onChange={e => setName(e.currentTarget.value)} placeholder="Optional" mb="sm" />
      <TextInput label="IP Address" required value={host} onChange={e => setHost(e.currentTarget.value)} placeholder="192.168.1.100" mb="sm" />
      <TextInput label="Username" required value={user} onChange={e => setUser(e.currentTarget.value)} mb="sm" />
      <TextInput label="Password" type="password" required value={pass} onChange={e => setPass(e.currentTarget.value)} mb="md" />
      <Button fullWidth loading={loading} onClick={handleAdd} data-testid="confirm-add-node">
        One-Click Deploy
      </Button>
    </Modal>
  );
}

export default function NodesPage() {
  const [nodes, setNodes] = useState<any[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);

  const loadNodes = useCallback(async () => {
    try {
      const res = await fetch('/api/v1/nodes');
      if (res.ok) setNodes(await res.json());
    } finally { setIsLoading(false); }
  }, []);

  useEffect(() => {
    loadNodes();
    const interval = setInterval(loadNodes, 15000);
    return () => clearInterval(interval);
  }, [loadNodes]);

  const toggleSchedulable = async (nodeId: string, value: boolean) => {
    try {
      const res = await fetch(`/api/v1/nodes/${nodeId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isSchedulable: value }),
      });
      if (res.ok) {
        notifications.show({ title: '更新成功', message: value ? '节点已加入调度' : '节点已移出调度', color: 'green' });
        loadNodes();
      } else {
        notifications.show({ title: '更新失败', message: '请重试', color: 'red' });
      }
    } catch {
      notifications.show({ title: '更新失败', message: '网络错误', color: 'red' });
    }
  };

  const handleDeleteNode = async (id: string, name: string) => {
    if (!confirm(`确定删除节点 "${name}"？`)) return;
    try {
      const res = await fetch(`/api/v1/nodes/${id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '删除成功', message: `节点 ${name} 已移除`, color: 'green' });
        loadNodes();
      } else {
        notifications.show({ title: '删除失败', message: '请检查', color: 'red' });
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' });
    }
  };

  if (isLoading) return <Text>Loading...</Text>;
  return (
    <div data-testid="nodes-page">
      <Group justify="space-between" mb="lg">
        <Title order={2}>Node Management ({nodes?.length ?? 0})</Title>
        <Group>
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={loadNodes}>刷新</Button>
          <CleanupButton onCleanup={loadNodes} />
          <Button onClick={() => setModalOpen(true)}>+ Add Target Server</Button>
        </Group>
      </Group>
      <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
        {nodes?.map(node => (
          <div key={node.id} style={{ position: 'relative' }}>
            <NodeCard node={node} onToggleSchedulable={toggleSchedulable} />
            <div style={{ position: 'absolute', top: 8, right: 8 }}>
              <Tooltip label="删除节点">
                <ActionIcon color="red" variant="subtle" size="sm"
                  onClick={() => handleDeleteNode(node.id, node.name || node.hostAddress)}>
                  <Icon path={mdiDeleteOutline} size={1} />
                </ActionIcon>
              </Tooltip>
            </div>
          </div>
        ))}
      </SimpleGrid>
      {nodes?.length === 0 && <Text c="dimmed" ta="center" mt="lg">暂无节点，请添加目标服务器</Text>}
      <AddNodeModal opened={modalOpen} onClose={() => setModalOpen(false)} onAdded={loadNodes} />
    </div>
  );
}
