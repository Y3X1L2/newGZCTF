import {
  ActionIcon,
  Alert,
  Button,
  Group,
  Modal,
  NumberInput,
  Pagination,
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
  mdiClockOutline,
  mdiConsoleNetworkOutline,
  mdiDeleteOutline,
  mdiDocker,
  mdiHistory,
  mdiMagnify,
  mdiOpenInNew,
  mdiPlus,
  mdiProgressWrench,
  mdiRefresh,
  mdiServerNetwork,
  mdiShieldSearch,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { CleanupButton } from '@Components/admin/CleanupButton'
import { NodeCard, NodeInfo } from '@Components/admin/NodeCard'
import {
  YinyuMetricTile,
  YinyuModalBody,
  YinyuPanel,
  YinyuRouteLoader,
  YinyuStatusPill,
  YinyuStatusTone,
} from '@Components/yinyu/YinyuUI'
import { YinyuStatusText } from '@Components/yinyu/YinyuReactBits'
import { enableTeamLabNetwork } from '@Utils/NodeTeamLabApi'

type StatusFilter = 'all' | 'online' | 'offline' | 'busy' | 'error'
type ResourceTypeFilter = 'all' | 'container' | 'vm' | 'pentest' | 'teamlab'
type ResourceStatusFilter = 'all' | 'active' | 'history'

interface NodeResourceItem {
  kind: 'container' | 'vm' | 'pentest' | 'teamlab'
  id: string
  name: string
  status: string
  isActive: boolean
  startedAt: string
  expectedStopAt?: string | null
  stoppedAt?: string | null
  duration: string
  image?: string | null
  runtimeId?: string | null
  entry?: string | null
  ip?: string | null
  port?: number | null
  gameId?: number | null
  gameTitle?: string | null
  challengeId?: number | null
  challengeTitle?: string | null
  challengeCategory?: string | null
  teamId?: number | null
  teamName?: string | null
  userId?: string | null
  userName?: string | null
  providerName?: string | null
  osType?: string | null
}

interface NodeResourceListResponse {
  nodeId: string
  nodeName: string
  page: number
  pageSize: number
  total: number
  runningCount: number
  containerCount: number
  vmCount: number
  pentestCount: number
  teamLabCount: number
  items: NodeResourceItem[]
}

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

function canUseTeamLab(node: NodeInfo) {
  return Boolean(node.canHostTeamLabDocker || node.canHostTeamLabVm || node.canHostTeamLabFabric || node.canHostTeamLab)
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

function portPoolLabel(node: NodeInfo) {
  if (node.portPoolStart && node.portPoolEnd) {
    const isPublicPool = node.portPoolMode === 'nginx'
    const name = isPublicPool ? '公网转发池' : 'Docker 端口池'
    const scope = node.portPoolMode === 'nginx' ? '，全局共享' : ''
    return `${name}：${node.portPoolStart}-${node.portPoolEnd}${scope}，已占用 ${node.usedPorts ?? 0}/${node.totalPorts ?? 0}`
  }

  if (node.portPoolMode === 'docker-random')
    return 'Docker 端口池：未配置固定范围，当前由 Docker 自动分配宿主端口'

  if (node.portPoolMode === 'nginx-unconfigured')
    return '公网转发池：监听端口范围无效'

  return '端口池：未配置'
}

function teamLabStatusLabel(node: NodeInfo) {
  const status = String(node.teamLabTunnelStatus ?? '').toLowerCase()

  if (canUseTeamLab(node)) return '可调度'
  if (!node.teamLabNetworkEnabled && status !== '2' && status !== 'probing') return '未启用'
  if (status === '2' || status === 'probing') return '待验证'
  if (status === '4' || status === 'error') return '异常'
  return '不可调度'
}

function teamLabStatusTone(node: NodeInfo): YinyuStatusTone {
  const status = String(node.teamLabTunnelStatus ?? '').toLowerCase()

  if (canUseTeamLab(node)) return 'success'
  if (status === '2' || status === 'probing') return 'warm'
  if (status === '4' || status === 'error') return 'danger'
  return 'neutral'
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

function resourceStatusTone(item: NodeResourceItem): YinyuStatusTone {
  const status = item.status.toLowerCase()
  if (item.isActive && (status.includes('running') || status.includes('creating') || status.includes('pending'))) {
    return 'success'
  }
  if (
    status.includes('error') ||
    status.includes('failed') ||
    status.includes('orphaned') ||
    status.includes('manualcleanup')
  )
    return 'danger'
  if (status.includes('cleanup')) return 'warm'
  if (status.includes('destroyed') || status.includes('stopped')) return 'neutral'
  return 'warm'
}

function resourceStatusLabel(item: NodeResourceItem) {
  const status = item.status.toLowerCase()
  if (status.includes('running')) return '运行中'
  if (status.includes('creatingnetworks')) return '创建网络中'
  if (status.includes('creatingcontainers')) return '创建容器中'
  if (status.includes('creating') || status.includes('pending')) return '开启中'
  if (status.includes('cleanuppending')) return '清理中'
  if (status.includes('orphaned')) return '孤儿资源'
  if (status.includes('manualcleanup')) return '需人工清理'
  if (status.includes('destroyed')) return '已销毁'
  if (status.includes('stopped')) return '已停止'
  if (status.includes('error') || status.includes('failed')) return '异常'
  return item.status
}

function formatTime(value?: string | null) {
  if (!value) return '-'
  const time = dayjs(value)
  return time.isValid() ? time.format('YYYY-MM-DD HH:mm:ss') : '-'
}

function resourceEntry(item: NodeResourceItem) {
  if (item.entry) return item.entry
  if (item.ip && item.port) return `${item.ip}:${item.port}`
  return item.ip ?? '-'
}

function resourceKindLabel(item: NodeResourceItem) {
  if (item.kind === 'container') return '容器'
  if (item.kind === 'vm') return '虚拟机'
  if (item.kind === 'teamlab') return 'TeamLab'
  return '渗透资产'
}

function resourceKindTone(item: NodeResourceItem): YinyuStatusTone {
  if (item.kind === 'container') return 'success'
  if (item.kind === 'vm') return 'warm'
  return 'neutral'
}

function resourceKindIcon(item: NodeResourceItem) {
  if (item.kind === 'container') return mdiDocker
  if (item.kind === 'vm') return mdiConsoleNetworkOutline
  if (item.kind === 'teamlab') return mdiServerNetwork
  return mdiShieldSearch
}

function ResourceMeta({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <Stack gap={2} className="yy-node-resource-meta">
      <Text size="xs" className="yy-readable-text">
        {label}
      </Text>
      <Text size="sm" fw={700} truncate title={value == null ? undefined : String(value)}>
        {value == null || value === '' ? '-' : value}
      </Text>
    </Stack>
  )
}

function NodeResourceRow({
  item,
  disabled,
  onDestroy,
}: {
  item: NodeResourceItem
  disabled: boolean
  onDestroy: (item: NodeResourceItem) => void
}) {
  const tone = resourceStatusTone(item)
  const kindLabel = resourceKindLabel(item)
  const canDestroy = item.isActive

  return (
    <article className="yy-node-resource-row" data-active={item.isActive}>
      <Group justify="space-between" align="flex-start" gap="md" wrap="nowrap" className="yy-node-resource-row-main">
        <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
          <div className="yy-node-resource-kind" data-kind={item.kind}>
            <Icon path={resourceKindIcon(item)} size={0.95} />
          </div>
          <Stack gap={4} style={{ minWidth: 0 }}>
            <Group gap="xs" wrap="nowrap">
              <Text fw={800} truncate title={item.name}>
                {item.name}
              </Text>
              <YinyuStatusText tone={tone} className="yy-node-resource-status-text">
                {resourceStatusLabel(item)}
              </YinyuStatusText>
            </Group>
            <Group gap="xs" wrap="wrap">
              <YinyuStatusPill tone={resourceKindTone(item)} state="open">
                {kindLabel}
              </YinyuStatusPill>
              {item.challengeCategory && (
                <Text size="xs" className="yy-node-resource-token">
                  {item.challengeCategory}
                </Text>
              )}
              {item.osType && (
                <Text size="xs" className="yy-node-resource-token">
                  {item.osType}
                </Text>
              )}
              {item.providerName && (
                <Text size="xs" className="yy-node-resource-token">
                  {item.providerName}
                </Text>
              )}
            </Group>
          </Stack>
        </Group>
        <Group gap="xs" wrap="nowrap">
          {item.entry && item.entry.startsWith('http') && (
            <Tooltip label="打开入口">
              <ActionIcon component="a" href={item.entry} target="_blank" rel="noopener noreferrer" variant="subtle">
                <Icon path={mdiOpenInNew} size={0.78} />
              </ActionIcon>
            </Tooltip>
          )}
          <Tooltip label={canDestroy ? (item.kind === 'pentest' ? '清理队伍环境' : '销毁实例') : '历史实例不可销毁'}>
            <ActionIcon color="red" variant="subtle" disabled={!canDestroy || disabled} onClick={() => onDestroy(item)}>
              <Icon path={mdiDeleteOutline} size={0.82} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 2, md: 4, xl: 6 }} spacing="sm" className="yy-node-resource-grid">
        <ResourceMeta label="开启者" value={item.teamName ?? item.userName ?? '平台调度'} />
        <ResourceMeta label="开启时间" value={formatTime(item.startedAt)} />
        <ResourceMeta label="持续时间" value={item.duration} />
        <ResourceMeta label="开放地址" value={resourceEntry(item)} />
        <ResourceMeta label={item.kind === 'pentest' ? '比赛/资产' : '比赛/题目'} value={[item.gameTitle, item.challengeTitle].filter(Boolean).join(' / ')} />
        <ResourceMeta label="运行标识" value={item.runtimeId} />
      </SimpleGrid>
    </article>
  )
}

function NodeResourcePanel({
  node,
  version,
  onNodeUpdated,
}: {
  node: NodeInfo | null
  version: number
  onNodeUpdated: () => void | Promise<void>
}) {
  const [data, setData] = useState<NodeResourceListResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [disabled, setDisabled] = useState(false)
  const [savingLimits, setSavingLimits] = useState(false)
  const [checkingTeamLab, setCheckingTeamLab] = useState(false)
  const [enablingTeamLab, setEnablingTeamLab] = useState(false)
  const [maxContainers, setMaxContainers] = useState<string | number>(node?.maxContainers ?? 20)
  const [maxVms, setMaxVms] = useState<string | number>(node?.maxVms ?? 5)
  const [teamLabTunnelIp, setTeamLabTunnelIp] = useState(node?.teamLabTunnelIp ?? '')
  const [type, setType] = useState<ResourceTypeFilter>('all')
  const [status, setStatus] = useState<ResourceStatusFilter>('all')
  const [page, setPage] = useState(1)
  const hasLoadedResources = useRef(false)
  const pageSize = 8

  const loadResources = useCallback(async (silent = false) => {
    if (!node) return
    const wasFirstLoad = !hasLoadedResources.current
    if (!silent || wasFirstLoad) setLoading(true)
    try {
      const params = new URLSearchParams({
        type,
        status,
        page: String(page),
        pageSize: String(pageSize),
      })
      const res = await fetch(`/api/v1/nodes/${node.id}/resources?${params}`)
      if (res.ok) {
        setData(await res.json())
      } else {
        notifications.show({ title: '资源读取失败', message: '无法获取该节点的运行资源', color: 'red' })
      }
    } finally {
      hasLoadedResources.current = true
      if (!silent || wasFirstLoad) setLoading(false)
    }
  }, [node, page, status, type])

  useEffect(() => {
    setPage(1)
    hasLoadedResources.current = false
  }, [node?.id, status, type])

  useEffect(() => {
    setMaxContainers(node?.maxContainers ?? 20)
    setMaxVms(node?.maxVms ?? 5)
    setTeamLabTunnelIp(node?.teamLabTunnelIp ?? '')
  }, [node?.id, node?.maxContainers, node?.maxVms, node?.teamLabTunnelIp])

  useEffect(() => {
    loadResources()
  }, [loadResources, version])

  useEffect(() => {
    if (!node) return
    const interval = window.setInterval(() => {
      loadResources(true)
    }, 15000)
    return () => window.clearInterval(interval)
  }, [loadResources, node])

  const destroyResource = async (item: NodeResourceItem) => {
    const actionLabel = item.kind === 'pentest' ? '清理该队伍的综合渗透环境' : `销毁 ${resourceKindLabel(item)}`
    if (!confirm(`确定${actionLabel} "${item.name}" 吗？`)) return

    setDisabled(true)
    try {
      let endpoint = item.kind === 'container' ? `/api/admin/instances/${item.id}` : `/api/v1/nodes/vms/${item.id}/admin`
      let method = 'DELETE'

      if (item.kind === 'pentest') {
        if (!item.gameId || !item.teamId) {
          notifications.show({ title: '清理失败', message: '该渗透资源缺少比赛或队伍信息', color: 'red' })
          return
        }
        endpoint = `/api/admin/pentest/games/${item.gameId}/teams/${item.teamId}/cleanup`
        method = 'POST'
      }

      const res = await fetch(endpoint, { method })
      if (res.ok) {
        notifications.show({ title: item.kind === 'pentest' ? '清理任务已执行' : '销毁任务已执行', message: `${item.name} 已进入清理流程`, color: 'green' })
        loadResources()
      } else {
        const body = await res.json().catch(() => ({}))
        notifications.show({ title: item.kind === 'pentest' ? '清理失败' : '销毁失败', message: body.message || '请检查实例状态和节点连通性', color: 'red' })
      }
    } catch {
      notifications.show({ title: item.kind === 'pentest' ? '清理失败' : '销毁失败', message: '网络错误', color: 'red' })
    } finally {
      setDisabled(false)
    }
  }

  const saveLimits = async () => {
    if (!node) return

    const containerLimit = Number(maxContainers)
    const vmLimit = Number(maxVms)
    if (!Number.isInteger(containerLimit) || !Number.isInteger(vmLimit)) {
      notifications.show({ title: '保存失败', message: '上限必须是整数', color: 'red' })
      return
    }

    if (containerLimit < node.currentContainers || vmLimit < node.currentVms) {
      notifications.show({
        title: '保存失败',
        message: '上限不能小于该节点当前运行中的容器或虚拟机数量',
        color: 'red',
      })
      return
    }

    setSavingLimits(true)
    try {
      const res = await fetch(`/api/v1/nodes/${node.id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ maxContainers: containerLimit, maxVms: vmLimit }),
      })
      if (res.ok) {
        notifications.show({ title: '保存成功', message: '节点调度上限已更新', color: 'green' })
        onNodeUpdated()
      } else {
        const body = await res.json().catch(() => ({}))
        notifications.show({ title: '保存失败', message: body.message || '请检查节点上限配置', color: 'red' })
      }
    } catch {
      notifications.show({ title: '保存失败', message: '网络错误', color: 'red' })
    } finally {
      setSavingLimits(false)
    }
  }

  const checkTeamLabNetwork = async () => {
    if (!node) return

    setCheckingTeamLab(true)
    try {
      const result = await enableTeamLabNetwork(node.id, true)
      notifications.show({
        title: 'TeamLab 网络检查完成',
        message: result.message || '节点已完成 TeamLab/VPN 网络检查。',
        color: result.success === false ? 'yellow' : 'green',
      })
      await onNodeUpdated()
    } catch (err) {
      notifications.show({
        title: 'TeamLab 网络检查失败',
        message: err instanceof Error ? err.message : '无法完成节点 TeamLab/VPN 网络探测',
        color: 'red',
      })
    } finally {
      setCheckingTeamLab(false)
    }
  }

  const enableTeamLabScheduling = async () => {
    if (!node) return

    const tunnelIp = teamLabTunnelIp.trim()
    if (!/^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/.test(tunnelIp)) {
      notifications.show({
        title: '启用失败',
        message: '请填写节点基础设施隧道的 IPv4 地址。',
        color: 'red',
      })
      return
    }

    setEnablingTeamLab(true)
    try {
      const result = await enableTeamLabNetwork(node.id, false, tunnelIp)
      notifications.show({
        title: 'TeamLab 调度已启用',
        message: result.message || '该节点已加入 VPN 靶场网络调度池。',
        color: 'green',
      })
      await onNodeUpdated()
    } catch (err) {
      notifications.show({
        title: 'TeamLab 调度启用失败',
        message: err instanceof Error ? err.message : '无法启用节点 TeamLab/VPN 调度',
        color: 'red',
      })
    } finally {
      setEnablingTeamLab(false)
    }
  }

  if (!node) {
    return (
      <YinyuPanel p="lg" className="admin-panel yy-node-resource-panel" cells={48}>
        <Stack align="center" gap="xs" py="xl">
          <Icon path={mdiShieldSearch} size={1.5} />
          <Text fw={800}>选择一个节点查看运行资源</Text>
          <Text size="sm" className="yy-readable-text" ta="center">
            点击上方节点卡片后，这里会按容器和虚拟机分类展示当前运行实例与可追溯历史记录。
          </Text>
        </Stack>
      </YinyuPanel>
    )
  }

  const totalPages = Math.max(1, Math.ceil((data?.total ?? 0) / pageSize))

  return (
    <YinyuPanel p="md" className="admin-panel yy-node-resource-panel" cells={64}>
      <Stack gap="md">
        <Group justify="space-between" align="flex-start" gap="md">
          <Stack gap={4}>
            <Group gap="xs">
              <Title order={3}>{node.name || node.hostAddress}</Title>
              <YinyuStatusText tone="success">资源溯源</YinyuStatusText>
            </Group>
            <Text size="sm" className="yy-readable-text">
              当前运行资源优先展示，历史记录按开启时间倒序分页。容器销毁后若底层记录已被物理清理，将不再出现在历史列表中。
            </Text>
          </Stack>
          <Group gap="xs">
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.78} />} onClick={() => loadResources()}>
              刷新资源
            </Button>
          </Group>
        </Group>

        <div className="yy-node-schedule-config">
          <Group justify="space-between" align="end" gap="md" wrap="wrap">
            <Stack gap={2}>
              <Text fw={800}>调度配置</Text>
              <Text size="xs" className="yy-readable-text">
                当前运行：容器 {node.currentContainers}/{node.maxContainers}，虚拟机 {node.currentVms}/{node.maxVms}
              </Text>
              <Text size="xs" className="yy-readable-text">
                {portPoolLabel(node)}
              </Text>
            </Stack>
            <Group align="end" gap="sm" wrap="wrap">
              <NumberInput
                label="容器开启上限"
                value={maxContainers}
                onChange={setMaxContainers}
                min={node.currentContainers}
                max={10000}
                step={1}
                allowDecimal={false}
                w={150}
              />
              <NumberInput
                label="虚拟机开启上限"
                value={maxVms}
                onChange={setMaxVms}
                min={node.currentVms}
                max={1000}
                step={1}
                allowDecimal={false}
                w={150}
              />
              <Button onClick={saveLimits} loading={savingLimits} disabled={disabled || savingLimits}>
                保存上限
              </Button>
            </Group>
          </Group>
        </div>

        <div className="yy-node-schedule-config">
          <Group justify="space-between" align="end" gap="md" wrap="wrap">
            <Stack gap={2} style={{ minWidth: 0 }}>
              <Group gap="xs">
                <Text fw={800}>TeamLab/VPN 靶场网络</Text>
                <YinyuStatusText tone={teamLabStatusTone(node)}>{teamLabStatusLabel(node)}</YinyuStatusText>
              </Group>
              <Text size="xs" className="yy-readable-text">
                {node.teamLabTunnelIp ? `隧道地址：${node.teamLabTunnelIp}` : '隧道地址：未配置'}
                {node.teamLabTunnelLastError ? `；最近提示：${node.teamLabTunnelLastError}` : ''}
              </Text>
            </Stack>
            <Group align="end" gap="sm" wrap="wrap">
              <TextInput
                label="隧道 IPv4"
                value={teamLabTunnelIp}
                onChange={(event) => setTeamLabTunnelIp(event.currentTarget.value)}
                placeholder="10.250.0.10"
                w={170}
              />
              <Button
                variant="default"
                leftSection={<Icon path={mdiShieldSearch} size={0.78} />}
                loading={checkingTeamLab}
                disabled={disabled || checkingTeamLab || enablingTeamLab}
                onClick={checkTeamLabNetwork}
              >
                检查网络
              </Button>
              <Button
                leftSection={<Icon path={mdiCheckboxMarkedCircleOutline} size={0.78} />}
                loading={enablingTeamLab}
                disabled={disabled || checkingTeamLab || enablingTeamLab}
                onClick={enableTeamLabScheduling}
              >
                启用调度
              </Button>
            </Group>
          </Group>
        </div>

        <SimpleGrid cols={{ base: 2, md: 5 }}>
          <YinyuMetricTile label="运行中" value={data?.runningCount ?? 0} detail="active" tone="success" />
          <YinyuMetricTile label="容器记录" value={data?.containerCount ?? 0} detail="docker" tone="neutral" />
          <YinyuMetricTile label="虚拟机记录" value={data?.vmCount ?? 0} detail="kvm" tone="warm" />
          <YinyuMetricTile label="渗透资产" value={data?.pentestCount ?? 0} detail="pentest" tone="neutral" />
          <YinyuMetricTile label="TeamLab" value={data?.teamLabCount ?? 0} detail="fabric" tone="neutral" />
        </SimpleGrid>

        <Group justify="space-between" align="end" className="yy-node-resource-toolbar">
          <Group gap="sm">
            <Select
              label="资源类型"
              value={type}
              onChange={(value) => setType((value as ResourceTypeFilter | null) ?? 'all')}
              data={[
                { value: 'all', label: '全部资源' },
                { value: 'container', label: '容器' },
                { value: 'vm', label: '虚拟机' },
                { value: 'pentest', label: '综合渗透' },
                { value: 'teamlab', label: 'TeamLab' },
              ]}
              w={150}
            />
            <Select
              label="记录状态"
              value={status}
              onChange={(value) => setStatus((value as ResourceStatusFilter | null) ?? 'all')}
              data={[
                { value: 'all', label: '全部记录' },
                { value: 'active', label: '当前开启' },
                { value: 'history', label: '历史记录' },
              ]}
              w={150}
            />
          </Group>
          <Group gap={6} className="yy-readable-text">
            <Icon path={mdiHistory} size={0.72} />
            <Text size="xs">第 {page} 页 / 共 {totalPages} 页</Text>
          </Group>
        </Group>

        {loading ? (
          <div className="yy-admin-nodes-state">
            <YinyuRouteLoader title="资源溯源" description="正在读取节点实例" />
          </div>
        ) : data && data.items.length > 0 ? (
          <Stack gap="sm">
            {data.items.map((item) => (
              <NodeResourceRow key={`${item.kind}-${item.id}`} item={item} disabled={disabled} onDestroy={destroyResource} />
            ))}
          </Stack>
        ) : (
          <div className="yy-admin-nodes-state">
            <Stack align="center" gap="xs">
              <Icon path={mdiClockOutline} size={1.2} />
              <Text fw={800}>暂无资源记录</Text>
              <Text size="sm" className="yy-readable-text">
                当前筛选条件下没有容器或虚拟机记录。
              </Text>
            </Stack>
          </div>
        )}

        {data && data.total > pageSize && (
          <Group justify="flex-end">
            <Pagination value={page} onChange={setPage} total={totalPages} size="sm" />
          </Group>
        )}
      </Stack>
    </YinyuPanel>
  )
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
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>节点管理</Title>
            <Text size="sm" className="yy-readable-text">
              统一查看节点心跳、资源负载和调度状态。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => loadNodes()}>
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
          <MetricTile label="参与调度" value={stats.schedulable} tone={stats.schedulable > 0 ? 'teal' : 'gray'} />
        </SimpleGrid>

        <YinyuPanel p="md" className="admin-panel yy-admin-nodes-panel" cells={72}>
          <Stack gap="md">
            <Group justify="space-between" align="end" className="yy-admin-nodes-filter">
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

            {isLoading ? (
              <div className="yy-admin-nodes-state">
                <YinyuRouteLoader title="节点管理" description="正在读取节点状态" />
              </div>
            ) : filteredNodes.length > 0 ? (
              <SimpleGrid className="yy-admin-node-grid" cols={{ base: 1, md: 2, xl: 3 }}>
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
              </SimpleGrid>
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
