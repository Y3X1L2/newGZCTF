import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Divider,
  Drawer,
  Group,
  NumberInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { useModals } from '@mantine/modals'
import {
  mdiAccessPointNetwork,
  mdiArrowLeft,
  mdiAutoFix,
  mdiCheck,
  mdiClose,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiEyeOutline,
  mdiHelpCircleOutline,
  mdiLanConnect,
  mdiPlus,
  mdiPublish,
  mdiRefresh,
  mdiRouterNetwork,
  mdiServerNetwork,
  mdiShieldLinkVariantOutline,
  mdiStop,
  mdiVectorLine,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MiniMap,
  NodeResizer,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
  type NodeProps,
  type NodeTypes,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { FC, memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  YinyuDrawerBody,
  YinyuPanel,
  YinyuRouteLoader,
  YinyuStatusPill,
  YinyuTableShell,
} from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { fetcher } from '@Api'
import { TeamLabRuntimeObservability } from './TeamLabRuntimeObservability'
import classes from './PenetrationAdminPage.module.css'
import {
  ImageTemplateLite,
  PenetrationConfigModel,
  PenetrationDefaultPolicy,
  PenetrationDeploymentEventModel,
  PenetrationDeploymentStatus,
  PenetrationEdgeModel,
  PenetrationEnforcementMode,
  PenetrationInterfaceModel,
  PenetrationNetworkModel,
  PenetrationNodeModel,
  PenetrationNodeType,
  PenetrationPlanModel,
  PenetrationPolicyAction,
  PenetrationPolicyScope,
  PenetrationProtocol,
  PenetrationRouteStatus,
  PenetrationRuntimeStatus,
  PenetrationScoreItemModel,
  PenetrationSubmissionLogModel,
  PenetrationTeamEnvironmentModel,
  PenetrationZoneType,
  penetrationAdminApi,
} from '@Api/PenetrationApi'
import {
  NODE_W,
  NODE_H,
  SelectedTarget,
  SegmentData,
  AssetData,
  teamLabZoneTypes,
  teamLabNodeTypes,
  runtimeStatusLabels,
  edgeHintTitle,
  edgeHintText,
  routeStatusLabels,
  routeStatusTone,
  normalizeZoneType,
  normalizeNodeType,
  zoneLabel,
  nodeTypeLabel,
  zoneOptions,
  nodeTypeOptions,
  protocolOptions,
  enforcementOptions,
  enforcementLabel,
  newId,
  newTopologyKey,
  isReadyTeamLabTemplate,
  normalizeConfig,
  defaultNetwork,
  defaultScoreItem,
  defaultNode,
  fallbackConfig,
  buildEnterpriseBlueprint,
  makeEdge,
  nodeTypes,
  PenetrationUsageGuide,
  toFlowNodes,
  toFlowEdges,
  withFlowLayout,
  remapSelectedTarget
} from './penetrationTopologyModel'


const BuilderInner: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const navigate = useNavigate()
  const modals = useModals()
  const { screenToFlowPosition } = useReactFlow()
  const [config, setConfig] = useState<PenetrationConfigModel>()
  const [templates, setTemplates] = useState<ImageTemplateLite[]>([])
  const templatesRef = useRef<ImageTemplateLite[]>([])
  const [plan, setPlan] = useState<PenetrationPlanModel>()
  const [environments, setEnvironments] = useState<PenetrationTeamEnvironmentModel[]>([])
  const [submissions, setSubmissions] = useState<PenetrationSubmissionLogModel[]>([])
  const [deploymentEvents, setDeploymentEvents] = useState<PenetrationDeploymentEventModel[]>([])
  const [deploymentEventTotal, setDeploymentEventTotal] = useState(0)
  const [deploymentEventPage, setDeploymentEventPage] = useState(1)
  const [selectedTarget, setSelectedTarget] = useState<SelectedTarget>()
  const [loading, setLoading] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [lastSavedAt, setLastSavedAt] = useState<Date>()
  const [usageOpened, setUsageOpened] = useState(false)
  const [linkMode, setLinkMode] = useState(false)
  const [linkSourceNodeId, setLinkSourceNodeId] = useState<number>()
  const forceDeployRef = useRef(false)
  const [nodes, setNodes, onNodesChangeBase] = useNodesState<Node<SegmentData | AssetData>>([])
  const [edges, setEdges, onEdgesChangeBase] = useEdgesState<Edge>([])

  const selectedNetwork =
    selectedTarget?.kind === 'network'
      ? config?.networks.find((network) => network.id === selectedTarget.id)
      : undefined
  const selectedNode =
    selectedTarget?.kind === 'node'
      ? config?.nodes.find((node) => node.id === selectedTarget.id)
      : undefined
  const selectedEdge =
    selectedTarget?.kind === 'edge'
      ? config?.edges.find((edge) => edge.id === selectedTarget.id)
      : undefined

  const templateOptions = useMemo(
    () =>
      templates.map((template) => ({
        value: String(template.id),
        label: template.name,
        disabled: !isReadyTeamLabTemplate(template),
      })),
    [templates]
  )

  const syncFlow = useCallback(
    (next: PenetrationConfigModel, sourceTemplates: ImageTemplateLite[] = []) => {
      const normalized = normalizeConfig(next)
      setNodes(toFlowNodes(normalized, sourceTemplates))
      setEdges(toFlowEdges(normalized))
    },
    [setEdges, setNodes]
  )

  const syncFlowWithTemplates = useCallback(
    (next: PenetrationConfigModel, sourceTemplates = templatesRef.current) => syncFlow(next, sourceTemplates),
    [syncFlow]
  )

  const load = useCallback(async () => {
    if (gameId <= 0) return
    setLoading(true)
    try {
      const [configRes, templateRes, planRes, envRes, submissionRes, eventRes] = await Promise.all([
        penetrationAdminApi.getConfig(gameId),
        fetcher('/api/v1/image-templates?page=1&pageSize=100'),
        penetrationAdminApi.plan(gameId),
        penetrationAdminApi.getEnvironments(gameId),
        penetrationAdminApi.getSubmissions(gameId, 30),
        penetrationAdminApi.getDeploymentEvents(gameId, 50, 0),
      ])
      const nextTemplates = ((templateRes as { items?: unknown[] })?.items ?? []) as ImageTemplateLite[]
      const nextConfig = normalizeConfig(configRes.data.networks.length ? configRes.data : fallbackConfig(gameId))
      templatesRef.current = nextTemplates
      setConfig(nextConfig)
      setTemplates(nextTemplates)
      setPlan(planRes.data)
      setEnvironments(envRes.data)
      setSubmissions(submissionRes.data.data ?? [])
      setDeploymentEvents(eventRes.data.data ?? [])
      setDeploymentEventTotal(eventRes.data.total ?? eventRes.data.length ?? 0)
      setDeploymentEventPage(1)
      setDirty(false)
      setSelectedTarget(nextConfig.networks[0] ? { kind: 'network', id: nextConfig.networks[0].id } : undefined)
      syncFlowWithTemplates(nextConfig)
    } catch (err) {
      showErrorMsg(err, (key) => key)
      setConfig(undefined)
      setPlan(undefined)
      setNodes([])
      setEdges([])
      setSelectedTarget(undefined)
      setDirty(false)
    } finally {
      setLoading(false)
    }
  }, [gameId, syncFlowWithTemplates])

  const loadDeploymentEvents = useCallback(async (page = deploymentEventPage) => {
    if (gameId <= 0) return
    const pageSize = 50
    const res = await penetrationAdminApi.getDeploymentEvents(gameId, pageSize, (page - 1) * pageSize)
    setDeploymentEvents(res.data.data ?? [])
    setDeploymentEventTotal(res.data.total ?? res.data.length ?? 0)
    setDeploymentEventPage(page)
  }, [deploymentEventPage, gameId])

  useEffect(() => {
    void load()
  }, [load])

  const updateConfig = (updater: (current: PenetrationConfigModel) => PenetrationConfigModel) => {
    setConfig((current) => {
      if (!current) return current
      const next = normalizeConfig(updater(withFlowLayout(current, nodes)))
      syncFlowWithTemplates(next)
      setDirty(true)
      return next
    })
  }

  const updateNetwork = (id: number, patch: Partial<PenetrationNetworkModel>) =>
    updateConfig((current) => ({
      ...current,
      networks: current.networks.map((network) => (network.id === id ? { ...network, ...patch } : network)),
    }))

  const updateNode = (id: number, patch: Partial<PenetrationNodeModel>) =>
    updateConfig((current) => ({
      ...current,
      nodes: current.nodes.map((node) => (node.id === id ? { ...node, ...patch } : node)),
    }))

  const updateEdge = (id: number, patch: Partial<PenetrationEdgeModel>) =>
    updateConfig((current) => ({
      ...current,
      edges: current.edges.map((edge) => (edge.id === id ? { ...edge, ...patch } : edge)),
    }))

  const save = async (silent = false, manageLoading = true) => {
    if (!config) return undefined
    if (manageLoading) setLoading(true)
    try {
      const outgoing = normalizeConfig(withFlowLayout(config, nodes))
      const res = await penetrationAdminApi.saveConfig(gameId, outgoing)
      const saved = normalizeConfig(res.data)
      setConfig(saved)
      setDirty(false)
      setLastSavedAt(new Date())
      setSelectedTarget(remapSelectedTarget(selectedTarget, outgoing, saved))
      syncFlowWithTemplates(saved)
      const planRes = await penetrationAdminApi.plan(gameId, saved)
      setPlan(planRes.data)
      if (!silent) showNotification({ color: 'teal', message: '渗透编排已保存', icon: <Icon path={mdiCheck} size={1} /> })
      return saved
    } catch (err) {
      showErrorMsg(err, (key) => key)
      return undefined
    } finally {
      if (manageLoading) setLoading(false)
    }
  }

  const executeAction = async (kind: 'validate' | 'plan' | 'publish' | 'deploy' | 'cancelDeploy' | 'stop') => {
    setLoading(true)
    try {
      if (kind === 'validate' || kind === 'plan') {
        if (!config) return
        const currentDraft = normalizeConfig(withFlowLayout(config, nodes))
        const res = await penetrationAdminApi.plan(gameId, currentDraft)
        setPlan(res.data)
        showNotification({
          color: res.data.validation.valid ? 'teal' : 'yellow',
          message: res.data.validation.valid ? '当前画布校验通过，草稿未自动保存' : '当前画布计划存在问题，草稿未自动保存',
          icon: <Icon path={mdiVectorLine} size={1} />,
        })
      } else if (kind === 'publish') {
        const saved = await save(true, false)
        if (!saved) return
        const planRes = await penetrationAdminApi.plan(gameId, saved)
        setPlan(planRes.data)
        if (!planRes.data.validation.valid) {
          showNotification({
            color: 'red',
            message: '当前草稿未通过校验，已保存但不会发布。请先修复部署计划中的错误。',
            icon: <Icon path={mdiVectorLine} size={1} />,
          })
          return
        }
        const res = await penetrationAdminApi.publish(gameId)
        setConfig(normalizeConfig(res.data))
        syncFlowWithTemplates(res.data)
        showNotification({ color: 'teal', message: '场景版本已发布', icon: <Icon path={mdiPublish} size={1} /> })
      } else if (kind === 'deploy') {
        const res = await penetrationAdminApi.deploy(gameId, forceDeployRef.current)
        showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiAccessPointNetwork} size={1} /> })
        await load()
      } else if (kind === 'cancelDeploy') {
        const res = await penetrationAdminApi.cancelDeploy(gameId)
        showNotification({ color: 'yellow', message: res.data.title, icon: <Icon path={mdiStop} size={1} /> })
        await load()
      } else {
        const res = await penetrationAdminApi.stop(gameId)
        showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiStop} size={1} /> })
        await load()
      }
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const onNodesChange = (changes: NodeChange<Node<SegmentData | AssetData>>[]) => onNodesChangeBase(changes)
  const onEdgesChange = (changes: EdgeChange[]) => {
    onEdgesChangeBase(changes)
    const removed = changes.filter((change) => change.type === 'remove').map((change) => Number(change.id))
    if (removed.length) updateConfig((current) => ({ ...current, edges: current.edges.filter((edge) => !removed.includes(edge.id)) }))
  }

  const addPolicyEdge = (sourceNodeId: number, targetNodeId: number) => {
    if (!config) return
    const source = config.nodes.find((node) => node.id === sourceNodeId)
    const target = config.nodes.find((node) => node.id === targetNodeId)
    if (!source || !target || source.id === target.id) return
    const edge = {
      ...makeEdge(newId(config.edges), source, target, '内网路由关系'),
      enforcementMode: PenetrationEnforcementMode.Both,
    }
    const next = normalizeConfig({ ...config, edges: [...config.edges, edge] })
    setConfig(next)
    setSelectedTarget({ kind: 'edge', id: edge.id })
    setLinkMode(false)
    setLinkSourceNodeId(undefined)
    setDirty(true)
    syncFlowWithTemplates(next)
  }

  const onConnect = (connection: Connection) => {
    if (!connection.source || !connection.target) return
    addPolicyEdge(Number(connection.source), Number(connection.target))
  }

  const onFlowNodeClick = (_: unknown, node: Node<SegmentData | AssetData>) => {
    if (node.id.startsWith('network-')) {
      setSelectedTarget({ kind: 'network', id: Number(node.id.replace('network-', '')) })
      return
    }

    const nodeId = Number(node.id)
    if (linkMode) {
      if (!linkSourceNodeId) {
        setLinkSourceNodeId(nodeId)
        setSelectedTarget({ kind: 'node', id: nodeId })
        return
      }
      addPolicyEdge(linkSourceNodeId, nodeId)
      return
    }

    setSelectedTarget({ kind: 'node', id: nodeId })
  }

  const onNodeDragStop = (_: unknown, dragged: Node<SegmentData | AssetData>) => {
    if (!config) return
    let next = withFlowLayout(config, nodes.map((node) => (node.id === dragged.id ? dragged : node)))
    if (!dragged.id.startsWith('network-')) {
      const nodeId = Number(dragged.id)
      const oldNode = next.nodes.find((item) => item.id === nodeId)
      const oldNetwork = next.networks.find((network) => network.id === oldNode?.networkId)
      if (oldNode && oldNetwork) {
        const absolute = { x: oldNetwork.positionX + dragged.position.x + NODE_W / 2, y: oldNetwork.positionY + dragged.position.y + NODE_H / 2 }
        const newNetwork =
          next.networks
            .slice()
            .reverse()
            .find((network) => absolute.x >= network.positionX && absolute.x <= network.positionX + network.width && absolute.y >= network.positionY && absolute.y <= network.positionY + network.height) ?? oldNetwork
        next = normalizeConfig({
          ...next,
          nodes: next.nodes.map((node) =>
            node.id === nodeId
              ? {
                  ...node,
                  networkId: newNetwork.id,
                  interfaces: node.interfaces.map((item) => (item.isPrimary ? { ...item, networkId: newNetwork.id } : item)),
                  positionX: Math.max(28, absolute.x - newNetwork.positionX - NODE_W / 2),
                  positionY: Math.max(76, absolute.y - newNetwork.positionY - NODE_H / 2),
                }
              : node
          ),
        })
      }
    }
    setConfig(next)
    setDirty(true)
    syncFlowWithTemplates(next)
  }

  const addNetwork = (zoneType = PenetrationZoneType.Custom, position?: { x: number; y: number }) => {
    if (!config) return
    const network = { ...defaultNetwork(newId(config.networks), config.networks.length, zoneType), ...(position ? { positionX: position.x, positionY: position.y } : {}) }
    const next = normalizeConfig({ ...config, networks: [...config.networks, network] })
    setConfig(next)
    setSelectedTarget({ kind: 'network', id: network.id })
    setDirty(true)
    syncFlowWithTemplates(next)
  }

  const addNode = (nodeType = PenetrationNodeType.Internal, networkId?: number, position?: { x: number; y: number }) => {
    if (!config) return
    const network = config.networks.find((item) => item.id === networkId) ?? selectedNetwork ?? config.networks[0]
    const node = defaultNode(newId(config.nodes), network, config.nodes.length, nodeType)
    if (position) {
      node.positionX = Math.max(28, position.x - network.positionX)
      node.positionY = Math.max(76, position.y - network.positionY)
    }
    const next = normalizeConfig({ ...config, nodes: [...config.nodes, node] })
    setConfig(next)
    setSelectedTarget({ kind: 'node', id: node.id })
    setDirty(true)
    syncFlowWithTemplates(next)
  }

  const removeSelected = () => {
    if (!config || !selectedTarget) return
    const target = selectedTarget
    const selectedName =
      target.kind === 'network'
        ? config.networks.find((network) => network.id === target.id)?.name ?? '内网网段'
        : target.kind === 'node'
          ? config.nodes.find((node) => node.id === target.id)?.name ?? '节点'
          : config.edges.find((edge) => edge.id === target.id)?.label ?? '内网路由关系'
    modals.openConfirmModal({
      title: '确认删除编排对象',
      children: (
        <Text size="sm" className="yy-readable-text">
          将删除“{selectedName}”。删除内网网段会同时删除其中资产和相关路由关系，此操作需要保存后才会生效。
        </Text>
      ),
      labels: { confirm: '删除', cancel: '取消' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        updateConfig((current) => {
          if (target.kind === 'network') {
            if (current.networks.length <= 1) return current
            const removedNodeIds = current.nodes.filter((node) => node.networkId === target.id).map((node) => node.id)
            return {
              ...current,
              networks: current.networks.filter((network) => network.id !== target.id),
              nodes: current.nodes.filter((node) => node.networkId !== target.id),
              edges: current.edges.filter((edge) => !removedNodeIds.includes(edge.sourceNodeId) && !removedNodeIds.includes(edge.targetNodeId)),
            }
          }
          if (target.kind === 'node') {
            return {
              ...current,
              nodes: current.nodes.filter((node) => node.id !== target.id),
              edges: current.edges.filter((edge) => edge.sourceNodeId !== target.id && edge.targetNodeId !== target.id),
            }
          }
          return { ...current, edges: current.edges.filter((edge) => edge.id !== target.id) }
        })
        setSelectedTarget(undefined)
      },
    })
  }

  const onDrop = (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    const payload = event.dataTransfer.getData('application/yinyu-pentest')
    if (!payload || !config) return
    let parsed: { kind: 'network' | 'node'; value: string }
    try {
      parsed = JSON.parse(payload) as { kind: 'network' | 'node'; value: string }
    } catch {
      showNotification({ color: 'yellow', message: '拖拽数据格式无效，请重新从左侧工具栏拖入。' })
      return
    }
    const position = screenToFlowPosition({ x: event.clientX, y: event.clientY })
    if (parsed.kind === 'network') addNetwork(parsed.value as PenetrationZoneType, position)
    else {
      const network =
        config.networks
          .slice()
          .reverse()
          .find((item) => position.x >= item.positionX && position.x <= item.positionX + item.width && position.y >= item.positionY && position.y <= item.positionY + item.height) ?? config.networks[0]
      addNode(parsed.value as PenetrationNodeType, network.id, position)
    }
  }

  const dragStart = (event: React.DragEvent, kind: 'network' | 'node', value: string) => {
    event.dataTransfer.setData('application/yinyu-pentest', JSON.stringify({ kind, value }))
    event.dataTransfer.effectAllowed = 'move'
  }

  const executeRestartRuntimeNode = async (runtimeNodeId: number) => {
    setLoading(true)
    try {
      const res = await penetrationAdminApi.rebuildTeamByRuntimeNode(runtimeNodeId)
      showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiRefresh} size={1} /> })
      await load()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const restartRuntimeNode = (runtimeNodeId: number, teamName: string, nodeName: string) => {
    modals.openConfirmModal({
      title: '确认重建整队环境',
      children: (
        <Stack gap={6}>
          <Text size="sm">将先清理队伍“{teamName}”的当前环境，再按该队已部署版本重建整队渗透环境。</Text>
          <Text size="xs" className="yy-readable-text">触发来源：{nodeName}。重建期间该队 VPN 内网会短暂不可用。</Text>
        </Stack>
      ),
      labels: { confirm: '确认重建', cancel: '取消' },
      confirmProps: { color: 'yellow' },
      onConfirm: () => void executeRestartRuntimeNode(runtimeNodeId),
    })
  }

  const runAction = (kind: 'validate' | 'plan' | 'publish' | 'deploy' | 'cancelDeploy' | 'stop') => {
    if (kind === 'deploy' || kind === 'cancelDeploy' || kind === 'stop') {
      modals.openConfirmModal({
        title: kind === 'deploy'
          ? '确认部署渗透环境'
          : kind === 'cancelDeploy'
            ? '确认取消部署任务'
            : '确认停止渗透环境',
        children: (
          <Stack gap={6}>
            <Text size="sm">
              {kind === 'deploy'
                ? '部署会为所有已审核队伍创建或更新渗透环境。已是当前发布版本且运行正常的队伍会自动跳过。'
                : kind === 'cancelDeploy'
                  ? '取消会请求当前部署任务停止调度新队伍；已完成队伍保持运行，进行中的队伍会根据执行阶段进入失败或清理状态。'
                  : '停止会清理所有队伍的渗透容器和网络，选手将无法继续访问当前环境。'}
            </Text>
            <Text size="xs" className="yy-readable-text">
              当前已记录 {environments.length} 支队伍运行环境，执行后可在“运行”页查看时间线和残留清理状态。
            </Text>
            {kind === 'deploy' && (
              <Checkbox
                defaultChecked={forceDeployRef.current}
                onChange={(event) => {
                  forceDeployRef.current = event.currentTarget.checked
                }}
                label="强制重建已运行队伍"
                description="默认会跳过当前发布版本且运行正常的队伍。勾选后会先清理再重建全部已审核队伍。"
              />
            )}
          </Stack>
        ),
        labels: {
          confirm: kind === 'deploy' ? '确认部署' : kind === 'cancelDeploy' ? '确认取消' : '确认停止',
          cancel: '取消',
        },
        confirmProps: { color: kind === 'deploy' ? 'teal' : kind === 'cancelDeploy' ? 'yellow' : 'red' },
        onConfirm: () => void executeAction(kind),
      })
      return
    }

    void executeAction(kind)
  }

  const executeCleanupTeam = async (teamId: number) => {
    if (gameId <= 0) return
    setLoading(true)
    try {
      const res = await penetrationAdminApi.cleanupTeam(gameId, teamId)
      showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiRefresh} size={1} /> })
      await load()
    } catch (err) {
      showErrorMsg(err, (key) => key)
      await load()
    } finally {
      setLoading(false)
    }
  }

  const cleanupTeam = (env: PenetrationTeamEnvironmentModel) => {
    modals.openConfirmModal({
      title: '确认重新清理残留资源',
      children: (
        <Stack gap={6}>
          <Text size="sm">将再次尝试清理队伍“{env.teamName}”的残留容器和网络。</Text>
          <Text size="xs" className="yy-readable-text">
            当前状态：{runtimeStatusLabels[env.status] ?? env.status}，已重试 {env.cleanupRetryCount} 次。清理事件会继续保留在运行时间线中。
          </Text>
        </Stack>
      ),
      labels: { confirm: '重新清理', cancel: '取消' },
      confirmProps: { color: 'red' },
      onConfirm: () => void executeCleanupTeam(env.teamId),
    })
  }

  const envText = selectedNode ? Object.entries(selectedNode.environmentVariables ?? {}).map(([key, value]) => `${key}=${value}`).join('\n') : ''
  const updateEnvText = (value: string) => {
    if (!selectedNode) return
    const environmentVariables = Object.fromEntries(
      value
        .split('\n')
        .map((line) => line.trim())
        .filter(Boolean)
        .map((line) => {
          const index = line.indexOf('=')
          return index < 0 ? [line, ''] : [line.slice(0, index).trim(), line.slice(index + 1)]
        })
        .filter(([key]) => key)
    )
    updateNode(selectedNode.id, { environmentVariables })
  }
  const deploymentStatus = config?.status ?? PenetrationDeploymentStatus.Draft
  const isDeploying = deploymentStatus === PenetrationDeploymentStatus.Deploying
  const canStop =
    deploymentStatus === PenetrationDeploymentStatus.Running ||
    deploymentStatus === PenetrationDeploymentStatus.Partial
  const canDeploy =
    !isDeploying &&
    !!config?.publishedVersion &&
    deploymentStatus !== PenetrationDeploymentStatus.Draft

  return (
    <div className={`${classes.fullscreen} yy-pentest-fullscreen`}>
      <div className={`${classes.topbar} yy-pentest-topbar`}>
        <Group gap="xs" wrap="nowrap">
          <Button variant="light" leftSection={<Icon path={mdiArrowLeft} size={0.85} />} onClick={() => navigate(`/admin/games/${gameId}/info`)}>
            退出编排
          </Button>
          <Badge variant="light">Penetration Builder</Badge>
          <YinyuStatusPill tone={config?.status === PenetrationDeploymentStatus.Running ? 'success' : 'neutral'}>
            {config?.status ?? 'Draft'} / v{config?.publishedVersion ?? 0}
          </YinyuStatusPill>
          <YinyuStatusPill tone={dirty ? 'warm' : 'success'} state={dirty ? 'busy' : 'idle'}>
            {dirty ? '未保存草稿' : lastSavedAt ? `已保存 ${lastSavedAt.toLocaleTimeString()}` : '草稿同步'}
          </YinyuStatusPill>
        </Group>
        <Group gap="xs">
          <Button variant="light" leftSection={<Icon path={mdiHelpCircleOutline} size={0.85} />} onClick={() => setUsageOpened(true)}>
            使用说明
          </Button>
          <Tooltip label="保存当前画布、属性和网卡配置，并刷新右侧部署计划">
            <Button leftSection={<Icon path={mdiContentSaveOutline} size={0.85} />} onClick={() => void save()}>
              保存
            </Button>
          </Tooltip>
          <Tooltip label="仅检查当前画布，不保存草稿；用于预览 CIDR、模板、IP、容量和部署计划">
            <Button variant="light" leftSection={<Icon path={mdiVectorLine} size={0.85} />} onClick={() => runAction('validate')}>
              校验/计划
            </Button>
          </Tooltip>
          <Tooltip label="校验通过后发布一个可部署的场景版本">
            <Button variant="light" leftSection={<Icon path={mdiPublish} size={0.85} />} onClick={() => runAction('publish')}>
              发布
            </Button>
          </Tooltip>
          <Tooltip label="按已发布版本为全部参赛队伍创建隔离网络和容器">
            <Button leftSection={<Icon path={mdiAccessPointNetwork} size={0.85} />} disabled={loading || !canDeploy} onClick={() => runAction('deploy')}>
              部署
            </Button>
          </Tooltip>
          <Tooltip label="取消当前正在执行的部署任务，已完成队伍保持运行">
            <Button color="yellow" variant="light" leftSection={<Icon path={mdiStop} size={0.85} />} disabled={loading || !isDeploying} onClick={() => runAction('cancelDeploy')}>
              取消部署
            </Button>
          </Tooltip>
          <Tooltip label="停止并清理已部署的渗透环境">
            <Button color="red" variant="light" leftSection={<Icon path={mdiStop} size={0.85} />} disabled={loading || !canStop} onClick={() => runAction('stop')}>
              停止
            </Button>
          </Tooltip>
        </Group>
      </div>

      <Drawer
        opened={usageOpened}
        onClose={() => setUsageOpened(false)}
        position="right"
        size="xl"
        title="渗透编排使用说明"
        zIndex={3200}
      >
        <PenetrationUsageGuide />
      </Drawer>

      {!config ? (
        <YinyuPanel p="xl" className="yy-pentest-full-loader">
          <YinyuRouteLoader title="渗透编排" description="正在读取场景配置" />
        </YinyuPanel>
      ) : (
        <div className={`${classes.studio} yy-pentest-studio`}>
          <YinyuPanel p="md" className={`${classes.toolbox} yy-pentest-toolbox`}>
            <ScrollArea.Autosize mah="calc(100dvh - 7.5rem)">
              <Stack gap="md">
                <Stack gap={4}>
                  <Title order={4}>拓扑设计</Title>
                </Stack>
                <div className="yy-pentest-flow-steps">
                  {['添加网段', '添加资产', '连接访问路径', '一键生成', '校验发布'].map((step, index) => (
                    <div className="yy-pentest-flow-step" key={step}>
                      <b>{index + 1}</b>
                      <span>{step}</span>
                    </div>
                  ))}
                </div>
                <Button fullWidth leftSection={<Icon path={mdiAutoFix} size={0.85} />} onClick={() => {
                  const next = buildEnterpriseBlueprint(gameId, templates, config)
                  setConfig(next)
                  setSelectedTarget({ kind: 'network', id: next.networks[0].id })
                  syncFlowWithTemplates(next)
                }}>
                  一键生成 TeamLab 内网靶场
                </Button>
                <Button
                  fullWidth
                  variant={linkMode ? 'filled' : 'light'}
                  leftSection={<Icon path={mdiLanConnect} size={0.85} />}
                  onClick={() => {
                    setLinkMode((value) => !value)
                    setLinkSourceNodeId(undefined)
                  }}
                >
                  添加路由关系/连线
                </Button>
                {linkMode ? (
                  <Text size="xs" className="yy-pentest-link-hint">
                    {linkSourceNodeId ? '请选择访问目标资产节点' : '请选择访问起点资产节点'}
                  </Text>
                ) : null}
                <Divider />
                <Text fw={900}>内网网段</Text>
                <SimpleGrid cols={2}>
                  {teamLabZoneTypes.map((zone) => (
                    <button key={zone} type="button" className="yy-pentest-tool-chip" draggable onDragStart={(event) => dragStart(event, 'network', zone)} onClick={() => addNetwork(zone)}>
                      {zoneLabel(zone)}
                    </button>
                  ))}
                </SimpleGrid>
                <Text fw={900}>资产节点</Text>
                <SimpleGrid cols={2}>
                  {teamLabNodeTypes.map((type) => (
                    <button key={type} type="button" className="yy-pentest-tool-chip" draggable onDragStart={(event) => dragStart(event, 'node', type)} onClick={() => addNode(type)}>
                      {nodeTypeLabel(type)}
                    </button>
                  ))}
                </SimpleGrid>
                <Divider />
                <SimpleGrid cols={2}>
                  <NumberInput label="队伍网段" min={16} max={28} value={config.teamSubnetPrefix} onChange={(value) => updateConfig((current) => ({ ...current, teamSubnetPrefix: Number(value || 24) }))} />
                  <NumberInput label="子网前缀" min={24} max={30} value={config.networkSubnetPrefix} onChange={(value) => updateConfig((current) => ({ ...current, networkSubnetPrefix: Number(value || 28) }))} />
                </SimpleGrid>
                <TextInput label="地址池 CIDR" value={config.baseCidr} onChange={(event) => updateConfig((current) => ({ ...current, baseCidr: event.currentTarget.value }))} />
                <NumberInput label="选手最大重置次数" min={0} max={100} value={config.maxResetCount} onChange={(value) => updateConfig((current) => ({ ...current, maxResetCount: Number(value || 0) }))} />
              </Stack>
            </ScrollArea.Autosize>
          </YinyuPanel>

          <YinyuPanel className={`${classes.canvas} yy-pentest-canvas yy-pentest-canvas-full`} p={0}>
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={nodeTypes}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              onConnect={onConnect}
              onNodeClick={onFlowNodeClick}
              onEdgeClick={(_, edge) => setSelectedTarget({ kind: 'edge', id: Number(edge.id) })}
              onNodeDragStop={onNodeDragStop}
              onDrop={onDrop}
              onDragOver={(event) => {
                event.preventDefault()
                event.dataTransfer.dropEffect = 'move'
              }}
              fitView
              fitViewOptions={{ padding: 0.18 }}
              minZoom={0.18}
              maxZoom={1.35}
              nodesDraggable
              nodesConnectable
              elementsSelectable
              proOptions={{ hideAttribution: true }}
            >
              <Controls />
              <MiniMap nodeStrokeWidth={3} pannable zoomable />
              <Background variant={BackgroundVariant.Dots} gap={18} size={1} />
            </ReactFlow>
            {loading ? <div className="yy-pentest-canvas-loading"><YinyuRouteLoader title="正在执行" description="请稍候" /></div> : null}
          </YinyuPanel>

          <YinyuPanel p="md" className={`${classes.inspector} yy-pentest-inspector yy-pentest-inspector-full`}>
            <ScrollArea.Autosize mah="calc(100dvh - 7.5rem)">
              <Tabs defaultValue="property" keepMounted={false}>
                <Tabs.List grow>
                  <Tabs.Tab value="property">资产配置</Tabs.Tab>
                  <Tabs.Tab value="plan">连通关系</Tabs.Tab>
                  <Tabs.Tab value="runtime">发布与运行</Tabs.Tab>
                </Tabs.List>

                <Tabs.Panel value="property" pt="md">
                  {selectedNetwork ? (
                    <Stack gap="sm">
                      <Group justify="space-between">
                        <Title order={4}>内网网段属性</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="名称" value={selectedNetwork.name} onChange={(event) => updateNetwork(selectedNetwork.id, { name: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <Select label="网段类型" data={zoneOptions} value={normalizeZoneType(selectedNetwork.zoneType)} onChange={(value) => value && updateNetwork(selectedNetwork.id, { zoneType: value as PenetrationZoneType, isEntry: false })} />
                        <TextInput label="节点数量" value={`${config.nodes.filter((node) => node.networkId === selectedNetwork.id).length} 个资产`} readOnly />
                      </SimpleGrid>
                      <SimpleGrid cols={2}>
                        <TextInput label="标识" value={selectedNetwork.slug} onChange={(event) => updateNetwork(selectedNetwork.id, { slug: event.currentTarget.value })} />
                        <TextInput label="CIDR" value={selectedNetwork.cidr ?? ''} placeholder={selectedNetwork.previewCidr || '自动分配'} onChange={(event) => updateNetwork(selectedNetwork.id, { cidr: event.currentTarget.value || null })} />
                      </SimpleGrid>
                      <Select
                        label="默认策略"
                        data={[
                          { value: PenetrationDefaultPolicy.DenyAll, label: '默认拒绝，按连线放行' },
                          { value: PenetrationDefaultPolicy.AllowInternal, label: '域内允许，跨域按策略' },
                        ]}
                        value={selectedNetwork.defaultPolicy}
                        onChange={(value) => value && updateNetwork(selectedNetwork.id, { defaultPolicy: value as PenetrationDefaultPolicy })}
                      />
                      <Textarea label="说明" minRows={3} value={selectedNetwork.description ?? ''} onChange={(event) => updateNetwork(selectedNetwork.id, { description: event.currentTarget.value })} />
                    </Stack>
                  ) : selectedNode ? (
                    <Stack gap="sm">
                      <Group justify="space-between">
                        <Title order={4}>资产节点属性</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="名称" value={selectedNode.name} onChange={(event) => updateNode(selectedNode.id, { name: event.currentTarget.value })} />
                       <Select label="资产角色" data={nodeTypeOptions} value={normalizeNodeType(selectedNode.nodeType)} onChange={(value) => value && updateNode(selectedNode.id, { nodeType: value as PenetrationNodeType, isEntry: false, publishPort: false })} />
                      <Textarea label="场景说明" minRows={2} value={selectedNode.description ?? ''} onChange={(event) => updateNode(selectedNode.id, { description: event.currentTarget.value })} />
                      <Divider label="选手端黑盒信息" />
                      <TextInput
                        label="选手端代号"
                        description="留空时平台会自动显示为题目编号，不暴露真实资产名称。"
                        value={selectedNode.playerAlias ?? ''}
                        onChange={(event) => updateNode(selectedNode.id, { playerAlias: event.currentTarget.value })}
                      />
                      <Textarea
                        label="选手端说明"
                        description="只填写允许选手看到的任务背景，禁止写入内部 IP、网卡、网段等管理信息。"
                        minRows={2}
                        value={selectedNode.playerDescription ?? ''}
                        onChange={(event) => updateNode(selectedNode.id, { playerDescription: event.currentTarget.value })}
                      />
                      <Select label="环境模板" searchable clearable data={templateOptions} value={selectedNode.imageTemplateId ? String(selectedNode.imageTemplateId) : null} onChange={(value) => updateNode(selectedNode.id, { imageTemplateId: value ? Number(value) : null })} />
                      <TextInput label="备用 Docker 镜像" value={selectedNode.imageName ?? ''} onChange={(event) => updateNode(selectedNode.id, { imageName: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <NumberInput label="CPU(0.1 核)" min={1} value={selectedNode.cpuCount} onChange={(value) => updateNode(selectedNode.id, { cpuCount: Number(value || 1) })} />
                        <NumberInput label="内存 MB" min={64} value={selectedNode.memoryLimit} onChange={(value) => updateNode(selectedNode.id, { memoryLimit: Number(value || 64) })} />
                        <NumberInput label="存储 MB" min={64} value={selectedNode.storageLimit} onChange={(value) => updateNode(selectedNode.id, { storageLimit: Number(value || 64) })} />
                        <NumberInput label="服务端口" min={1} max={65535} value={selectedNode.exposePort} onChange={(value) => updateNode(selectedNode.id, { exposePort: Number(value || 80) })} />
                      </SimpleGrid>
                      <Group>
                        <Checkbox label="允许作为路由节点" checked={selectedNode.allowRouting} onChange={(event) => updateNode(selectedNode.id, { allowRouting: event.currentTarget.checked })} />
                      </Group>
                      <Divider label="网卡 / IPAM" />
                      {selectedNode.interfaces.map((item, index) => (
                        <YinyuPanel key={`${item.id}-${index}`} p="xs" className="yy-pentest-score-editor">
                          <SimpleGrid cols={2}>
                            <TextInput label="网卡名" value={item.name} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, name: event.currentTarget.value } : it) })} />
                            <Select label="所属内网网段" data={config.networks.map((network) => ({ value: String(network.id), label: network.name }))} value={String(item.networkId)} onChange={(value) => value && updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, networkId: Number(value) } : it), networkId: item.isPrimary ? Number(value) : selectedNode.networkId })} />
                            <TextInput label="固定运行 IP" value={item.staticIp ?? ''} placeholder={item.previewIp || '自动分配'} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, staticIp: event.currentTarget.value } : it) })} />
                            <Group mt="1.6rem">
                              <Checkbox label="主网卡" checked={item.isPrimary} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => ({ ...it, isPrimary: it.id === item.id ? event.currentTarget.checked : false })) })} />
                              <Checkbox label="管理通道" checked={item.isManagement} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, isManagement: event.currentTarget.checked } : it) })} />
                            </Group>
                          </SimpleGrid>
                        </YinyuPanel>
                      ))}
                      <Button variant="light" leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => updateNode(selectedNode.id, { interfaces: [...selectedNode.interfaces, { id: newId(selectedNode.interfaces), topologyKey: newTopologyKey('interface'), nodeId: selectedNode.id, networkId: selectedNode.networkId, name: `eth${selectedNode.interfaces.length}`, staticIp: '', isPrimary: false, isManagement: false, orderIndex: selectedNode.interfaces.length }] })}>
                        添加网卡
                      </Button>
                      <Divider label="得分项" />
                      <YinyuPanel p="xs" className="yy-pentest-preview-box">
                        <Text size="xs" className="yy-readable-text">
                          勾选“解锁检查点”后，该节点全部检查点完成才会解锁下一跳；如果一个节点没有检查点，则默认完成该节点所有可见得分项后解锁下一跳。
                        </Text>
                      </YinyuPanel>
                      {selectedNode.scoreItems.map((item) => (
                        <YinyuPanel key={item.id} p="xs" className="yy-pentest-score-editor">
                          <Stack gap={6}>
                            <Group justify="space-between">
                              <Badge variant="light">{item.category || '综合'}</Badge>
                              <ActionIcon size="sm" color="red" variant="subtle" onClick={() => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.filter((score) => score.id !== item.id) })}>
                                <Icon path={mdiClose} size={0.7} />
                              </ActionIcon>
                            </Group>
                            <TextInput label="任务标题" value={item.title} onChange={(event) => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.map((score) => score.id === item.id ? { ...score, title: event.currentTarget.value } : score) })} />
                            <SimpleGrid cols={2}>
                              <TextInput label="方向" value={item.category} onChange={(event) => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.map((score) => score.id === item.id ? { ...score, category: event.currentTarget.value } : score) })} />
                              <NumberInput label="分值" min={0} value={item.score} onChange={(value) => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.map((score) => score.id === item.id ? { ...score, score: Number(value || 0) } : score) })} />
                            </SimpleGrid>
                            <Checkbox
                              label="作为解锁检查点"
                              checked={item.isCheckpoint}
                              onChange={(event) => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.map((score) => score.id === item.id ? { ...score, isCheckpoint: event.currentTarget.checked } : score) })}
                            />
                            <Textarea label="描述" value={item.description ?? ''} onChange={(event) => updateNode(selectedNode.id, { scoreItems: selectedNode.scoreItems.map((score) => score.id === item.id ? { ...score, description: event.currentTarget.value } : score) })} />
                          </Stack>
                        </YinyuPanel>
                      ))}
                      <Button variant="light" leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => updateNode(selectedNode.id, { scoreItems: [...selectedNode.scoreItems, defaultScoreItem(selectedNode.scoreItems.length)] })}>
                        添加得分项
                      </Button>
                      <Textarea label="环境变量" description="每行一个 KEY=value，平台会自动追加各得分项 Flag。" value={envText} minRows={3} onChange={(event) => updateEnvText(event.currentTarget.value)} />
                    </Stack>
                  ) : selectedEdge ? (
                    <Stack gap="sm">
                      <Group justify="space-between">
                        <Title order={4}>内网路由关系</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="策略名称" value={selectedEdge.label ?? ''} onChange={(event) => updateEdge(selectedEdge.id, { label: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <Select label="备注协议" description="仅用于出题备注" data={protocolOptions} value={selectedEdge.protocol} onChange={(value) => value && updateEdge(selectedEdge.id, { protocol: value as PenetrationProtocol })} />
                        <TextInput label="备注端口范围" description="仅用于出题备注" value={selectedEdge.portRange} onChange={(event) => updateEdge(selectedEdge.id, { portRange: event.currentTarget.value })} />
                      </SimpleGrid>
                      <TextInput label="关系类型" value="TeamLab 内网路由关系" readOnly />
                      <SimpleGrid cols={2}>
                        <Select
                          label="执行模式"
                          data={enforcementOptions}
                          value={selectedEdge.enforcementMode ?? PenetrationEnforcementMode.Both}
                          onChange={(value) => value && updateEdge(selectedEdge.id, { enforcementMode: value as PenetrationEnforcementMode })}
                        />
                        <NumberInput label="优先级" min={0} max={10000} value={selectedEdge.priority ?? 100} onChange={(value) => updateEdge(selectedEdge.id, { priority: Number(value || 100) })} />
                      </SimpleGrid>
                      <YinyuPanel p="xs" className="yy-pentest-preview-box">
                        <Text size="xs" fw={800}>{edgeHintTitle}</Text>
                        <Text size="xs" className="yy-readable-text">{edgeHintText}</Text>
                      </YinyuPanel>
                      <Textarea label="说明" minRows={3} value={selectedEdge.description ?? ''} onChange={(event) => updateEdge(selectedEdge.id, { description: event.currentTarget.value })} />
                    </Stack>
                  ) : (
                    <Stack gap="xs" align="center" justify="center" mih={300}>
                      <Icon path={mdiRouterNetwork} size={2.2} />
                      <Text fw={900}>选择内网网段、资产或连线</Text>
                      <Text className="yy-readable-text" ta="center">点击画布对象后，可在此配置网段、网卡、IP、路由关系和得分项。</Text>
                    </Stack>
                  )}
                </Tabs.Panel>

                <Tabs.Panel value="plan" pt="md">
                  <Stack gap="sm">
                    <Group justify="space-between">
                      <Title order={4}>部署计划</Title>
                      <Button size="xs" variant="light" leftSection={<Icon path={mdiEyeOutline} size={0.75} />} onClick={() => runAction('plan')}>刷新</Button>
                    </Group>
                    {plan ? (
                      <>
                        <YinyuPanel p="sm" className="yy-pentest-preview-box">
                          <Group justify="space-between"><Text fw={900}>部署预览网段</Text><Badge variant="light">{plan.sampleTeamPrefix}</Badge></Group>
                          <Text size="sm" className="yy-readable-text">参赛队伍：{plan.teamCount}，内网网段：{plan.networks.length}，资产：{plan.nodes.length}。运行页展示已部署环境的真实地址。</Text>
                        </YinyuPanel>
                        {[...(plan.validation.errors ?? []), ...(plan.validation.warnings ?? [])].map((message, index) => (
                          <Text key={`${message}-${index}`} className={plan.validation.errors.includes(message) ? 'yy-pentest-error' : 'yy-readable-text'} size="sm">
                            {message}
                          </Text>
                        ))}
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>内网网段</Table.Th><Table.Th>CIDR</Table.Th><Table.Th>接入方式</Table.Th><Table.Th>策略</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>{plan.networks.map((network) => <Table.Tr key={network.networkId}><Table.Td>{network.networkName}</Table.Td><Table.Td>{network.cidr}</Table.Td><Table.Td>队伍 VPN 内网</Table.Td><Table.Td>{network.defaultPolicy}</Table.Td></Table.Tr>)}</Table.Tbody>
                          </Table>
                        </YinyuTableShell>
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>资产</Table.Th><Table.Th>网卡/IP</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>{plan.nodes.map((node) => <Table.Tr key={node.nodeId}><Table.Td>{node.nodeName}</Table.Td><Table.Td>{node.interfaces.map((item) => `${item.name}:${item.ipAddress}`).join(' / ')}</Table.Td></Table.Tr>)}</Table.Tbody>
                          </Table>
                        </YinyuTableShell>
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>路由关系</Table.Th><Table.Th>来源</Table.Th><Table.Th>目标</Table.Th><Table.Th>执行结果</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>
                              {plan.policies.length > 0 ? plan.policies.map((policy) => (
                                <Table.Tr key={policy.policyId}>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      <Text size="sm" fw={800}>{policy.label}</Text>
                                      <Text size="xs" className="yy-readable-text">{policy.protocol.toUpperCase()} / {policy.portRange}</Text>
                                    </Stack>
                                  </Table.Td>
                                  <Table.Td>{policy.source}</Table.Td>
                                  <Table.Td>{policy.target}</Table.Td>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      <YinyuStatusPill tone={routeStatusTone(policy.routeStatus)} state={policy.routeStatus === PenetrationRouteStatus.RoutePlanned ? 'running' : 'idle'}>
                                        {routeStatusLabels[policy.routeStatus]}
                                      </YinyuStatusPill>
                                      <Text size="xs" fw={800} c={policy.isExecutable ? 'teal.2' : 'dimmed'}>
                                        {policy.isExecutable ? '会写入网段级路由' : '未生成运行期路由'}
                                      </Text>
                                      <Text size="xs" className="yy-readable-text">{enforcementLabel(policy.enforcementMode)} · {policy.runtimeSummary}</Text>
                                      {policy.routeNodeName && <Text size="xs" className="yy-readable-text">路由节点：{policy.routeNodeName}，网关：{policy.gatewayIp ?? '自动'}</Text>}
                                      {policy.compileMessage && <Text size="xs" className="yy-readable-text" lineClamp={2}>{policy.compileMessage}</Text>}
                                    </Stack>
                                  </Table.Td>
                                </Table.Tr>
                              )) : (
                                <Table.Tr><Table.Td colSpan={4}>暂无路由关系。至少连接两个资产节点，用于表达任务链和跳板关系。</Table.Td></Table.Tr>
                              )}
                            </Table.Tbody>
                          </Table>
                        </YinyuTableShell>
                        <YinyuPanel p="sm" className="yy-pentest-preview-box">
                          <Text fw={900} mb={6}>部署执行顺序</Text>
                          <Stack gap={4}>
                            {plan.deploymentSteps.map((step, index) => (
                              <Text size="sm" className="yy-readable-text" key={step}>
                                {index + 1}. {step}
                              </Text>
                            ))}
                          </Stack>
                        </YinyuPanel>
                      </>
                    ) : <Text className="yy-readable-text">暂无部署计划。</Text>}
                  </Stack>
                </Tabs.Panel>

                <Tabs.Panel value="runtime" pt="md">
                  <TeamLabRuntimeObservability
                    gameId={gameId}
                    maxResetCount={config.maxResetCount}
                    environments={environments}
                    submissions={submissions}
                    deploymentEvents={deploymentEvents}
                    deploymentEventTotal={deploymentEventTotal}
                    deploymentEventPage={deploymentEventPage}
                    onRefresh={load}
                    onLoadDeploymentEvents={loadDeploymentEvents}
                    onCleanupTeam={cleanupTeam}
                    onRestartRuntimeNode={restartRuntimeNode}
                  />
                </Tabs.Panel>
              </Tabs>
            </ScrollArea.Autosize>
          </YinyuPanel>
        </div>
      )}
    </div>
  )
}

const PenetrationAdminPage: FC = () => (
  <ReactFlowProvider>
    <BuilderInner />
  </ReactFlowProvider>
)

export default PenetrationAdminPage
