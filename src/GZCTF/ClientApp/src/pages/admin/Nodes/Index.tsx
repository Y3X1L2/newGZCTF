import { useState } from 'react';
import { SimpleGrid, Title, Text, Button, TextInput, Modal, Group } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { NodeCard } from '../../../components/admin/NodeCard';
import { useNodes } from '../../../hooks/useNodes';

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
          message: `Node ${data.nodeName} connected, capabilities: ${data.capabilities}`,
          color: 'green'
        });
        onAdded();
        onClose();
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
  const { nodes, isLoading, mutate } = useNodes();
  const [modalOpen, setModalOpen] = useState(false);
  if (isLoading) return <Text>Loading...</Text>;
  return (
    <div data-testid="nodes-page">
      <Group justify="space-between" mb="lg">
        <Title order={2}>Node Management ({nodes?.length ?? 0})</Title>
        <Button onClick={() => setModalOpen(true)}>+ Add Target Server</Button>
      </Group>
      <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }}>
        {nodes?.map(node => <NodeCard key={node.id} node={node} />)}
      </SimpleGrid>
      <AddNodeModal opened={modalOpen} onClose={() => setModalOpen(false)} onAdded={mutate} />
    </div>
  );
}
