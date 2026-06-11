import {
  ActionIcon,
  Alert,
  Button,
  Group,
  Loader,
  Modal,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiCheckboxMarkedCircleOutline,
  mdiDeleteOutline,
  mdiMagnify,
  mdiPlus,
  mdiProgressWrench,
  mdiRefresh,
  mdiServerNetwork,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { CleanupButton } from '@Components/admin/CleanupButton'
import { NodeCard, NodeInfo } from '@Components/admin/NodeCard'
import { YinyuMetricTile, YinyuModalBody, YinyuPanel, YinyuStatePage } from '@Components/yinyu/YinyuUI'

type StatusFilter = 'all' | 'online' | 'offline' | 'busy' | 'error'

const statusKeys: Record<string, StatusFilter> = {
  '1': 'online',
  online: 'online',
  '2': 'offline',
  offline: 'offline',
  '3': 'busy',
  busy: 'busy',
  '4': 'error',
  error: 'error',
}

function statusKey(status: string | number | undefined): StatusFilter {
  return statusKeys[String(status ?? '').toLowerCase()] ?? 'error'
}

function AddNodeModal({ opened, onClose, onAdded }: { opened: boolean; onClose: () => void; onAdded: () => void }) {
  const [host, setHost] = useState('')
  const [user, setUser] = useState('root')
  const [pass, setPass] = useState('')
  const [name, setName] = useState('')
  const [loading, setLoading] = useState(false)

  const handleAdd = async () => {
    if (!host.trim() || !user.trim() || !pass) return

    setLoading(true)
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          hostAddress: host.trim(),
          username: user.trim(),
          password: pass,
          nodeName: name.trim() || null,
        }),
      })
      const data = await res.json().catch(() => ({}))
      if (res.ok) {
        notifications.show({
          title: '部署成功',
          message: `节点 ${data.nodeName || host} 已接入，能力：${data.capabilities ?? '已检测'}`,
          color: 'green',
        })
        onAdded()
        onClose()
        setHost('')
        setUser('root')
        setPass('')
        setName('')
      } else {
        notifications.show({
          title: '部署失败',
          message: data.message || '请检查服务器地址、账号权限、包源和 Docker/KVM 支持状态',
          color: 'red',
          autoClose: 9000,
        })
      }
    } catch {
      notifications.show({
        title: '连接失败',
        message: '无法连接平台 API',
        color: 'red',
      })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal
      opened={opened}
      onClose={loading ? () => undefined : onClose}
      title="添加目标服务器"
      data-testid="add-node-modal"
      radius="sm"
      centered
      closeOnClickOutside={!loading}
    >
      <YinyuModalBody p="md">
        <Stack gap="md">
          <Alert
            variant="light"
            color="blue"
            radius="sm"
            icon={<Icon path={loading ? mdiProgressWrench : mdiServerNetwork} size={0.85} />}
          >
            <Stack gap={4}>
              <Text size="sm" fw={700}>
                {loading ? '正在自动部署节点' : '一站式接入工作节点'}
              </Text>
              <Text size="xs" className="yy-readable-text">
                {loading
                  ? '平台正在通过 SSH 探测环境、安装 Docker/KVM/libvirt、写入 Agent 配置并等待心跳。'
                  : '提交后会自动探测并安装分布式运行所需依赖，完成后节点会出现在调度池中。'}
              </Text>
            </Stack>
          </Alert>
          <TextInput
            label="节点名称"
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            placeholder="可选"
            disabled={loading}
          />
          <TextInput
            label="IP 地址"
            required
            value={host}
            onChange={(event) => setHost(event.currentTarget.value)}
            placeholder="10.0.7.125"
            disabled={loading}
          />
          <TextInput
            label="用户名"
            required
            value={user}
            onChange={(event) => setUser(event.currentTarget.value)}
            disabled={loading}
          />
          <TextInput
            label="密码"
            type="password"
            required
            value={pass}
            onChange={(event) => setPass(event.currentTarget.value)}
            disabled={loading}
          />
          <Alert
            variant="outline"
            color="gray"
            radius="sm"
            icon={<Icon path={mdiCheckboxMarkedCircleOutline} size={0.78} />}
          >
            <Text size="xs" className="yy-readable-text">
              目标账号需要 root 或免密 sudo 权限；重复添加同一 IP 会复用原节点并重新安装 Agent。
            </Text>
          </Alert>
          <Button
            fullWidth
            leftSection={<Icon path={mdiPlus} size={0.8} />}
            loading={loading}
            onClick={handleAdd}
            data-testid="confirm-add-node"
          >
            {loading ? '正在部署，等待节点心跳' : '一键部署'}
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}

function MetricTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  const toneMap: Record<string, 'success' | 'warm' | 'danger' | 'neutral'> = {
    teal: 'success',
    green: 'success',
    blue: 'neutral',
    yellow: 'warm',
    red: 'danger',
    gray: 'neutral',
  }

  return <YinyuMetricTile label={label} value={value} detail={tone} tone={toneMap[tone] ?? 'neutral'} />
}

export default function NodesPage() {
  const [nodes, setNodes] = useState<NodeInfo[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [modalOpen, setModalOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<StatusFilter>('all')

  const loadNodes = useCallback(async () => {
    try {
      const res = await fetch('/api/v1/nodes')
      if (res.ok) setNodes(await res.json())
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    loadNodes()
    const interval = setInterval(loadNodes, 15000)
    return () => clearInterval(interval)
  }, [loadNodes])

  const stats = useMemo(() => {
    const online = nodes.filter((node) => statusKey(node.status) === 'online').length
    return {
      total: nodes.length,
      online,
      offline: nodes.length - online,
      schedulable: nodes.filter((node) => node.isSchedulable && statusKey(node.status) === 'online').length,
    }
  }, [nodes])

  const filteredNodes = useMemo(() => {
    const keyword = query.trim().toLowerCase()
    return nodes.filter((node) => {
      const matchedStatus = filter === 'all' || statusKey(node.status) === filter
      const matchedKeyword =
        !keyword || node.name?.toLowerCase().includes(keyword) || node.hostAddress?.toLowerCase().includes(keyword)
      return matchedStatus && matchedKeyword
    })
  }, [filter, nodes, query])

  const toggleSchedulable = async (nodeId: string, value: boolean) => {
    try {
      const res = await fetch(`/api/v1/nodes/${nodeId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isSchedulable: value }),
      })
      if (res.ok) {
        notifications.show({ title: '更新成功', message: value ? '节点已加入调度' : '节点已移出调度', color: 'green' })
        loadNodes()
      } else {
        notifications.show({ title: '更新失败', message: '请稍后重试', color: 'red' })
      }
    } catch {
      notifications.show({ title: '更新失败', message: '网络错误', color: 'red' })
    }
  }

  const handleDeleteNode = async (id: string, name: string) => {
    if (!confirm(`确定删除节点 "${name}" 吗？`)) return

    try {
      const res = await fetch(`/api/v1/nodes/${id}`, { method: 'DELETE' })
      if (res.ok) {
        notifications.show({ title: '删除成功', message: `节点 ${name} 已移除`, color: 'green' })
        loadNodes()
      } else {
        const data = await res.json().catch(() => ({}))
        notifications.show({ title: '删除失败', message: data.message || '请检查节点状态', color: 'red' })
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' })
    }
  }

  return (
    <AdminPage>
      <Stack data-testid="nodes-page" gap="lg" w="100%">
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>节点管理</Title>
            <Text size="sm" className="yy-readable-text">
              统一查看节点心跳、资源负载和调度状态。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
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

        <YinyuPanel p="md" className="admin-panel">
          <Group justify="space-between" align="end">
            <TextInput
              leftSection={<Icon path={mdiMagnify} size={0.75} />}
              placeholder="搜索节点名称或地址"
              value={query}
              onChange={(event) => setQuery(event.currentTarget.value)}
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
        </YinyuPanel>

        {isLoading ? (
          <Group justify="center" py="xl">
            <Loader />
          </Group>
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
          <YinyuStatePage p="xl">
            <Stack align="center" gap="xs">
              <Text fw={700}>没有匹配的节点</Text>
              <Text className="yy-readable-text" size="sm">
                调整筛选条件，或添加新的目标服务器。
              </Text>
            </Stack>
          </YinyuStatePage>
        )}

        <AddNodeModal opened={modalOpen} onClose={() => setModalOpen(false)} onAdded={loadNodes} />
      </Stack>
    </AdminPage>
  )
}
