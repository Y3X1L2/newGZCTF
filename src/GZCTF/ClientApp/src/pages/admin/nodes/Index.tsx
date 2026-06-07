import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  Paper,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { mdiDeleteOutline, mdiMagnify, mdiPlus, mdiRefresh } from '@mdi/js';
import { Icon } from '@mdi/react';
import { CleanupButton } from '../../../components/admin/CleanupButton';
import { NodeCard, NodeInfo } from '../../../components/admin/NodeCard';

type StatusFilter = 'all' | 'online' | 'offline' | 'busy' | 'error';

const statusKeys: Record<string, StatusFilter> = {
  '1': 'online',
  online: 'online',
  '2': 'offline',
  offline: 'offline',
  '3': 'busy',
  busy: 'busy',
  '4': 'error',
  error: 'error',
};

function statusKey(status: string | number | undefined): StatusFilter {
  return statusKeys[String(status ?? '').toLowerCase()] ?? 'error';
}

function AddNodeModal({ opened, onClose, onAdded }: { opened: boolean; onClose: () => void; onAdded: () => void }) {
  const [host, setHost] = useState('');
  const [user, setUser] = useState('root');
  const [pass, setPass] = useState('');
  const [name, setName] = useState('');
  const [loading, setLoading] = useState(false);

  const handleAdd = async () => {
    if (!host.trim() || !user.trim() || !pass) return;

    setLoading(true);
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ hostAddress: host.trim(), username: user.trim(), password: pass, nodeName: name.trim() || null }),
      });
      const data = await res.json().catch(() => ({}));
      if (res.ok) {
        notifications.show({
          title: '部署成功',
          message: `节点 ${data.nodeName || host} 已接入`,
          color: 'green',
        });
        onAdded();
        onClose();
        setHost('');
        setUser('root');
        setPass('');
        setName('');
      } else {
        notifications.show({
          title: '部署失败',
          message: data.message || '请检查服务器地址和账号权限',
          color: 'red',
        });
      }
    } catch {
      notifications.show({
        title: '连接失败',
        message: '无法连接平台 API',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="添加目标服务器" data-testid="add-node-modal" radius="sm">
      <Stack>
        <TextInput label="节点名称" value={name} onChange={(e) => setName(e.currentTarget.value)} placeholder="可选" />
        <TextInput label="IP 地址" required value={host} onChange={(e) => setHost(e.currentTarget.value)} placeholder="192.168.1.100" />
        <TextInput label="用户名" required value={user} onChange={(e) => setUser(e.currentTarget.value)} />
        <TextInput label="密码" type="password" required value={pass} onChange={(e) => setPass(e.currentTarget.value)} />
        <Button fullWidth leftSection={<Icon path={mdiPlus} size={0.8} />} loading={loading} onClick={handleAdd} data-testid="confirm-add-node">
          一键部署
        </Button>
      </Stack>
    </Modal>
  );
}

function MetricTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <Paper withBorder radius="sm" p="md">
      <Group justify="space-between">
        <Text size="sm" c="dimmed">{label}</Text>
        <Badge color={tone} variant="light">{value}</Badge>
      </Group>
      <Text mt={4} fw={800} size="xl">{value}</Text>
    </Paper>
  );
}

export default function NodesPage() {
  const [nodes, setNodes] = useState<NodeInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<StatusFilter>('all');

  const loadNodes = useCallback(async () => {
    try {
      const res = await fetch('/api/v1/nodes');
      if (res.ok) setNodes(await res.json());
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadNodes();
    const interval = setInterval(loadNodes, 15000);
    return () => clearInterval(interval);
  }, [loadNodes]);

  const stats = useMemo(() => {
    const online = nodes.filter((node) => statusKey(node.status) === 'online').length;
    return {
      total: nodes.length,
      online,
      offline: nodes.length - online,
      schedulable: nodes.filter((node) => node.isSchedulable && statusKey(node.status) === 'online').length,
    };
  }, [nodes]);

  const filteredNodes = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    return nodes.filter((node) => {
      const matchedStatus = filter === 'all' || statusKey(node.status) === filter;
      const matchedKeyword = !keyword
        || node.name?.toLowerCase().includes(keyword)
        || node.hostAddress?.toLowerCase().includes(keyword);
      return matchedStatus && matchedKeyword;
    });
  }, [filter, nodes, query]);

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
        notifications.show({ title: '更新失败', message: '请稍后重试', color: 'red' });
      }
    } catch {
      notifications.show({ title: '更新失败', message: '网络错误', color: 'red' });
    }
  };

  const handleDeleteNode = async (id: string, name: string) => {
    if (!confirm(`确定删除节点 "${name}" 吗？`)) return;

    try {
      const res = await fetch(`/api/v1/nodes/${id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '删除成功', message: `节点 ${name} 已移除`, color: 'green' });
        loadNodes();
      } else {
        const data = await res.json().catch(() => ({}));
        notifications.show({ title: '删除失败', message: data.message || '请检查节点状态', color: 'red' });
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' });
    }
  };

  return (
    <Stack data-testid="nodes-page" gap="lg">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>节点管理</Title>
          <Text size="sm" c="dimmed">统一查看节点心跳、资源负载和调度状态。</Text>
        </Stack>
        <Group>
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={loadNodes}>
            刷新
          </Button>
          <CleanupButton onCleanup={loadNodes} />
          <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => setModalOpen(true)}>
            添加服务器
          </Button>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 2, md: 4 }}>
        <MetricTile label="全部节点" value={stats.total} tone="gray" />
        <MetricTile label="在线节点" value={stats.online} tone="teal" />
        <MetricTile label="离线/异常" value={stats.offline} tone={stats.offline > 0 ? 'red' : 'gray'} />
        <MetricTile label="可调度" value={stats.schedulable} tone="blue" />
      </SimpleGrid>

      <Paper withBorder radius="sm" p="md">
        <Group justify="space-between" align="end">
          <TextInput
            leftSection={<Icon path={mdiMagnify} size={0.75} />}
            placeholder="搜索节点名称或地址"
            value={query}
            onChange={(e) => setQuery(e.currentTarget.value)}
            style={{ minWidth: 260 }}
          />
          <Select
            label="状态"
            value={filter}
            onChange={(value) => setFilter((value as StatusFilter | null) ?? 'all')}
            data={[
              { value: 'all', label: '全部' },
              { value: 'online', label: '在线' },
              { value: 'offline', label: '离线' },
              { value: 'busy', label: '繁忙' },
              { value: 'error', label: '异常' },
            ]}
            w={160}
          />
        </Group>
      </Paper>

      {isLoading ? (
        <Group justify="center" py="xl"><Loader /></Group>
      ) : filteredNodes.length > 0 ? (
        <SimpleGrid cols={{ base: 1, md: 2, xl: 3 }}>
          {filteredNodes.map((node) => (
            <NodeCard
              key={node.id}
              node={node}
              onToggleSchedulable={toggleSchedulable}
              rightSection={
                !node.isLocal && (
                  <Tooltip label="删除节点">
                    <ActionIcon
                      color="red"
                      variant="subtle"
                      size="sm"
                      onClick={() => handleDeleteNode(node.id, node.name || node.hostAddress)}
                    >
                      <Icon path={mdiDeleteOutline} size={0.82} />
                    </ActionIcon>
                  </Tooltip>
                )
              }
            />
          ))}
        </SimpleGrid>
      ) : (
        <Paper withBorder radius="sm" p="xl">
          <Stack align="center" gap="xs">
            <Text fw={700}>没有匹配的节点</Text>
            <Text c="dimmed" size="sm">调整筛选条件，或添加新的目标服务器。</Text>
          </Stack>
        </Paper>
      )}

      <AddNodeModal opened={modalOpen} onClose={() => setModalOpen(false)} onAdded={loadNodes} />
    </Stack>
  );
}
