import {
  ActionIcon,
  Button,
  Group,
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
  mdiDeleteOutline,
  mdiMagnify,
  mdiPlus,
  mdiProgressWrench,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { CleanupButton } from '@Components/admin/CleanupButton'
import { NodeCard, NodeInfo } from '@Components/admin/NodeCard'
import { DataToolbar, DeferredGrid, MetricGrid, PageHeader } from '@Components/foundation'
import { YinyuMetricTile, YinyuPanel, YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import { AddNodeModal } from './AddNodeModal'
import { NodeResourcePanel } from './NodeResourcePanel'

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

function sortNodesStable(nodes: NodeInfo[]) {
  return [...nodes].sort((left, right) => {
    const localOrder = Number(Boolean(right.isLocal)) - Number(Boolean(left.isLocal))
    if (localOrder !== 0) return localOrder

    const nameOrder = (left.name || left.hostAddress || '').localeCompare(right.name || right.hostAddress || '', 'zh-Hans-CN')
    if (nameOrder !== 0) return nameOrder

    return left.id.localeCompare(right.id)
  })
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
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [resourceVersion, setResourceVersion] = useState(0)
  const [syncingNodeIds, setSyncingNodeIds] = useState<string[]>([])
  const hasLoadedNodes = useRef(false)

  const loadNodes = useCallback(async (silent = false) => {
    const wasFirstLoad = !hasLoadedNodes.current
    if (!silent && wasFirstLoad) setIsLoading(true)
    try {
      const res = await fetch('/api/v1/nodes')
      if (res.ok) setNodes(sortNodesStable(await res.json()))
    } finally {
      hasLoadedNodes.current = true
      if (!silent || wasFirstLoad) setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    loadNodes()
    const interval = setInterval(() => loadNodes(true), 15000)
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

  const selectedNode = useMemo(
    () => nodes.find((node) => node.id === selectedNodeId) ?? filteredNodes[0] ?? null,
    [filteredNodes, nodes, selectedNodeId]
  )

  useEffect(() => {
    if (!selectedNodeId && filteredNodes.length > 0) {
      setSelectedNodeId(filteredNodes[0].id)
      return
    }

    if (selectedNodeId && !nodes.some((node) => node.id === selectedNodeId)) {
      setSelectedNodeId(filteredNodes[0]?.id ?? null)
    }
  }, [filteredNodes, nodes, selectedNodeId])

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
        if (selectedNodeId === id) setSelectedNodeId(null)
        loadNodes()
      } else {
        const data = await res.json().catch(() => ({}))
        notifications.show({ title: '删除失败', message: data.message || '请检查节点状态', color: 'red' })
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' })
    }
  }

  const syncAgent = async (node: NodeInfo) => {
    setSyncingNodeIds((ids) => (ids.includes(node.id) ? ids : [...ids, node.id]))
    try {
      const res = await fetch(`/api/v1/nodes/${node.id}/sync-agent`, { method: 'POST' })
      const data = await res.json().catch(() => ({}))
      if (res.ok) {
        notifications.show({
          title: '同步已下发',
          message: data.message || '节点 Agent 正在同步最新版本并重启。',
          color: 'green',
        })
        await loadNodes()
      } else {
        notifications.show({
          title: '同步失败',
          message: data.message || '无法同步节点 Agent，请检查节点在线状态。',
          color: 'red',
          autoClose: 9000,
        })
      }
    } catch {
      notifications.show({ title: '同步失败', message: '网络错误', color: 'red' })
    } finally {
      setSyncingNodeIds((ids) => ids.filter((id) => id !== node.id))
    }
  }

  return (
    <AdminPage>
      <Stack data-testid="nodes-page" gap="lg" w="100%">
        <PageHeader
          eyebrow="Infrastructure"
          title="节点管理"
          description="统一查看节点心跳、资源负载和调度状态。"
          actions={
            <>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => loadNodes()}>
              刷新
            </Button>
            <CleanupButton onCleanup={loadNodes} />
            <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => setModalOpen(true)}>
              添加服务器
            </Button>
            </>
          }
        />

        <MetricGrid>
          <MetricTile label="全部节点" value={stats.total} tone="gray" />
          <MetricTile label="在线节点" value={stats.online} tone="teal" />
          <MetricTile label="离线/异常" value={stats.offline} tone={stats.offline > 0 ? 'red' : 'gray'} />
          <MetricTile label="参与调度" value={stats.schedulable} tone={stats.schedulable > 0 ? 'teal' : 'gray'} />
        </MetricGrid>

        <YinyuPanel p="md" className="admin-panel yy-admin-nodes-panel" cells={72}>
          <Stack gap="md">
            <DataToolbar className="yy-admin-nodes-filter">
              <TextInput
                leftSection={<Icon path={mdiMagnify} size={0.75} />}
                placeholder="搜索节点名称或地址"
                value={query}
                onChange={(event) => setQuery(event.currentTarget.value)}
                miw={260}
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
            </DataToolbar>

            {isLoading ? (
              <div className="yy-admin-nodes-state">
                <YinyuRouteLoader title="节点管理" description="正在读取节点状态" />
              </div>
            ) : filteredNodes.length > 0 ? (
              <DeferredGrid className="yy-admin-node-grid" cols={{ base: 1, md: 2, xl: 3 }}>
                {filteredNodes.map((node) => (
                  <NodeCard
                    key={node.id}
                    node={node}
                    onToggleSchedulable={toggleSchedulable}
                    selected={selectedNode?.id === node.id}
                    onSelect={(item) => {
                      setSelectedNodeId(item.id)
                      setResourceVersion((value) => value + 1)
                    }}
                    rightSection={
                      <Group gap={4} wrap="nowrap">
                        {!node.isLocal && (
                          <>
                            <Tooltip label="同步最新版本">
                              <ActionIcon
                                color="blue"
                                variant="subtle"
                                size="sm"
                                loading={syncingNodeIds.includes(node.id)}
                                onClick={(event) => {
                                  event.stopPropagation()
                                  syncAgent(node)
                                }}
                              >
                                <Icon path={mdiProgressWrench} size={0.82} />
                              </ActionIcon>
                            </Tooltip>
                            <Tooltip label="删除节点">
                              <ActionIcon
                                color="red"
                                variant="subtle"
                                size="sm"
                                onClick={(event) => {
                                  event.stopPropagation()
                                  handleDeleteNode(node.id, node.name || node.hostAddress)
                                }}
                              >
                                <Icon path={mdiDeleteOutline} size={0.82} />
                              </ActionIcon>
                            </Tooltip>
                          </>
                        )}
                      </Group>
                    }
                  />
                ))}
              </DeferredGrid>
            ) : (
              <div className="yy-admin-nodes-state">
                <Stack align="center" gap="xs">
                  <Text fw={700}>没有匹配的节点</Text>
                  <Text className="yy-readable-text" size="sm">
                    调整筛选条件，或添加新的目标服务器。
                  </Text>
                </Stack>
              </div>
            )}
          </Stack>
        </YinyuPanel>

        <NodeResourcePanel node={selectedNode} version={resourceVersion} onNodeUpdated={loadNodes} />

        <AddNodeModal opened={modalOpen} onClose={() => setModalOpen(false)} onAdded={loadNodes} />
      </Stack>
    </AdminPage>
  )
}
