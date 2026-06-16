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
import {
  ImageTemplateLite,
  PenetrationAdminAccessModel,
  PenetrationConfigModel,
  PenetrationDefaultPolicy,
  PenetrationDeploymentStatus,
  PenetrationEdgeModel,
  PenetrationInterfaceModel,
  PenetrationNetworkModel,
  PenetrationNodeModel,
  PenetrationNodeType,
  PenetrationPlanModel,
  PenetrationPolicyAction,
  PenetrationPolicyScope,
  PenetrationProtocol,
  PenetrationRuntimeStatus,
  PenetrationScoreItemModel,
  PenetrationSubmissionLogModel,
  PenetrationTeamEnvironmentModel,
  PenetrationZoneType,
  penetrationAdminApi,
} from '../../../../Api/PenetrationApi'

const NETWORK_W = 620
const NETWORK_H = 420
const NODE_W = 238
const NODE_H = 148

type SelectedTarget =
  | { kind: 'network'; id: number }
  | { kind: 'node'; id: number }
  | { kind: 'edge'; id: number }
  | undefined

type SegmentData = Record<string, unknown> & {
  label: string
  slug: string
  cidr: string
  zoneType: PenetrationZoneType
  nodeCount: number
}

type AssetData = Record<string, unknown> & {
  label: string
  nodeType: PenetrationNodeType
  templateName: string
  interfaceLabel: string
  scoreLabel: string
  isEntry: boolean
}

const zoneLabels: Record<PenetrationZoneType, string> = {
  [PenetrationZoneType.Public]: '公网',
  [PenetrationZoneType.Dmz]: 'DMZ',
  [PenetrationZoneType.Business]: '业务区',
  [PenetrationZoneType.Data]: '数据区',
  [PenetrationZoneType.Operations]: '运维区',
  [PenetrationZoneType.Management]: '管理区',
  [PenetrationZoneType.Custom]: '自定义',
}

const nodeTypeLabels: Record<PenetrationNodeType, string> = {
  [PenetrationNodeType.Entry]: '入口服务',
  [PenetrationNodeType.Web]: 'Web 服务',
  [PenetrationNodeType.Database]: '数据库',
  [PenetrationNodeType.JumpHost]: '跳板机',
  [PenetrationNodeType.Internal]: '内网资产',
  [PenetrationNodeType.DomainControllerReserved]: 'AD 预留',
  [PenetrationNodeType.Custom]: '自定义资产',
  [PenetrationNodeType.Bastion]: '堡垒机',
  [PenetrationNodeType.FirewallRouter]: '防火墙/路由',
  [PenetrationNodeType.Service]: '业务服务',
}

const zoneOptions = Object.values(PenetrationZoneType).map((value) => ({ value, label: zoneLabels[value] }))
const nodeTypeOptions = Object.values(PenetrationNodeType).map((value) => ({ value, label: nodeTypeLabels[value] }))
const protocolOptions = Object.values(PenetrationProtocol).map((value) => ({ value, label: value.toUpperCase() }))

const flowNetworkId = (id: number) => `network-${id}`
const newId = (items: { id: number }[]) => Math.min(-1, ...items.map((item) => item.id)) - 1
const enumKey = (value: string | number | undefined | null) => String(value ?? '').toLowerCase()
const isReadyLinuxDockerTemplate = (template: ImageTemplateLite) =>
  (enumKey(template.osType) === '0' || enumKey(template.osType) === 'linux') &&
  (enumKey(template.imageType) === '0' || enumKey(template.imageType) === 'docker') &&
  (enumKey(template.status) === '0' || enumKey(template.status) === 'ready')
const normalizeConfig = (config: PenetrationConfigModel): PenetrationConfigModel => ({
  ...config,
  interfaces: config.nodes.flatMap((node) => node.interfaces.map((item) => ({ ...item, nodeId: node.id }))),
})

const defaultNetwork = (id: number, orderIndex: number, zoneType = PenetrationZoneType.Custom): PenetrationNetworkModel => ({
  id,
  name: zoneType === PenetrationZoneType.Public ? '公网入口区' : `${zoneLabels[zoneType]} ${orderIndex + 1}`,
  slug: zoneType.toLowerCase(),
  cidr: '',
  zoneType,
  trustLevel: zoneType === PenetrationZoneType.Public ? 10 : zoneType === PenetrationZoneType.Data ? 80 : 50,
  description: '',
  defaultPolicy: PenetrationDefaultPolicy.DenyAll,
  orderIndex,
  isEntry: zoneType === PenetrationZoneType.Public,
  positionX: 80 + orderIndex * 700,
  positionY: 90 + (orderIndex % 2) * 60,
  width: NETWORK_W,
  height: NETWORK_H,
  collapsed: false,
})

const defaultScoreItem = (orderIndex: number): PenetrationScoreItemModel => ({
  id: -Date.now() - orderIndex,
  title: `得分项 ${orderIndex + 1}`,
  description: '',
  category: '综合',
  score: 100,
  isDynamic: true,
  staticFlag: '',
  flagTemplate: 'flag{[TEAM_HASH]}',
  maxAttempts: 0,
  isVisible: true,
  prerequisiteItemIds: [],
  orderIndex,
})

const defaultNode = (
  id: number,
  network: PenetrationNetworkModel,
  orderIndex: number,
  nodeType = PenetrationNodeType.Internal
): PenetrationNodeModel => {
  const isEntry = nodeType === PenetrationNodeType.Entry
  return {
    id,
    networkId: network.id,
    name: isEntry ? '外网入口服务' : nodeTypeLabels[nodeType],
    description: '',
    nodeType,
    imageTemplateId: null,
    imageName: '',
    cpuCount: 10,
    memoryLimit: 512,
    storageLimit: 512,
    exposePort: isEntry ? 8080 : 80,
    isEntry,
    publishPort: isEntry,
    staticIp: '',
    environmentVariables: {},
    startCommand: '',
    healthCheck: '',
    reservedAdRole: '',
    positionX: 48 + (orderIndex % 2) * 260,
    positionY: 100 + Math.floor(orderIndex / 2) * 172,
    orderIndex,
    previewIp: '',
    interfaces: [
      {
        id: -Date.now() - orderIndex,
        nodeId: id,
        networkId: network.id,
        name: 'eth0',
        staticIp: '',
        previewIp: '',
        isPrimary: true,
        isManagement: isEntry,
        orderIndex: 0,
      },
    ],
    scoreItems: [defaultScoreItem(0)],
  }
}

const fallbackConfig = (gameId: number): PenetrationConfigModel => {
  const network = defaultNetwork(-1, 0, PenetrationZoneType.Public)
  return {
    gameId,
    baseCidr: '10.60.0.0/12',
    teamSubnetPrefix: 24,
    networkSubnetPrefix: 28,
    maxResetCount: 3,
    publishedVersion: 0,
    status: PenetrationDeploymentStatus.Draft,
    networks: [network],
    nodes: [defaultNode(-11, network, 0, PenetrationNodeType.Entry)],
    interfaces: [],
    edges: [],
  }
}

const buildEnterpriseBlueprint = (gameId: number, templates: ImageTemplateLite[], current?: PenetrationConfigModel) => {
  const template = templates.find(isReadyLinuxDockerTemplate)
  const networks = [
    { ...defaultNetwork(-101, 0, PenetrationZoneType.Public), name: '公网入口区', slug: 'public', positionX: 60, positionY: 110 },
    { ...defaultNetwork(-102, 1, PenetrationZoneType.Dmz), name: 'DMZ 服务区', slug: 'dmz', positionX: 760, positionY: 70 },
    { ...defaultNetwork(-103, 2, PenetrationZoneType.Business), name: '业务内网区', slug: 'biz', positionX: 1460, positionY: 160 },
    { ...defaultNetwork(-104, 3, PenetrationZoneType.Data), name: '数据内网区', slug: 'data', positionX: 2160, positionY: 80 },
    { ...defaultNetwork(-105, 4, PenetrationZoneType.Operations), name: '运维管理区', slug: 'ops', positionX: 1460, positionY: 650 },
  ]
  const withTemplate = (node: PenetrationNodeModel) => ({
    ...node,
    imageTemplateId: template?.id ?? null,
    imageName: template ? '' : node.imageName,
  })
  const nodes = [
    withTemplate({ ...defaultNode(-201, networks[0], 0, PenetrationNodeType.Entry), name: '外网主服务', positionX: 58, positionY: 120 }),
    withTemplate({
      ...defaultNode(-202, networks[1], 1, PenetrationNodeType.JumpHost),
      name: 'DMZ 跳板机',
      positionX: 60,
      positionY: 110,
      interfaces: [
        { id: -501, nodeId: -202, networkId: networks[1].id, name: 'eth0', isPrimary: true, isManagement: false, orderIndex: 0 },
        { id: -502, nodeId: -202, networkId: networks[2].id, name: 'eth1', isPrimary: false, isManagement: false, orderIndex: 1 },
        { id: -503, nodeId: -202, networkId: networks[4].id, name: 'eth2', isPrimary: false, isManagement: true, orderIndex: 2 },
      ],
    }),
    withTemplate({ ...defaultNode(-203, networks[1], 2, PenetrationNodeType.Web), name: '废弃 Web 服务', positionX: 314, positionY: 238, publishPort: true, exposePort: 63000 }),
    withTemplate({ ...defaultNode(-204, networks[2], 3, PenetrationNodeType.Service), name: '业务应用', positionX: 170, positionY: 128 }),
    withTemplate({ ...defaultNode(-205, networks[3], 4, PenetrationNodeType.Database), name: '数据库节点', positionX: 178, positionY: 128, exposePort: 3306 }),
    withTemplate({ ...defaultNode(-206, networks[4], 5, PenetrationNodeType.Bastion), name: '运维堡垒机', positionX: 170, positionY: 122, exposePort: 22 }),
  ]
  const edges: PenetrationEdgeModel[] = [
    makeEdge(-301, nodes[0], nodes[1], '入口到跳板', '22,80,443'),
    makeEdge(-302, nodes[0], nodes[2], '公网暴露服务', '63000'),
    makeEdge(-303, nodes[1], nodes[3], '跳板到业务区', 'any'),
    makeEdge(-304, nodes[3], nodes[4], '业务访问数据区', '3306'),
    makeEdge(-305, nodes[5], nodes[1], '运维管理跳板', '22'),
  ]
  return normalizeConfig({
    ...(current ?? fallbackConfig(gameId)),
    gameId,
    networks,
    nodes,
    edges,
  })
}

const makeEdge = (id: number, source: PenetrationNodeModel, target: PenetrationNodeModel, label: string, portRange = 'any'): PenetrationEdgeModel => ({
  id,
  sourceNodeId: source.id,
  targetNodeId: target.id,
  sourceKind: PenetrationPolicyScope.Node,
  sourceId: source.id,
  targetKind: PenetrationPolicyScope.Node,
  targetId: target.id,
  protocol: PenetrationProtocol.Tcp,
  portRange,
  policyAction: PenetrationPolicyAction.Allow,
  isRouteHint: true,
  label,
  description: '',
})

const SegmentNode = memo(({ data, selected }: NodeProps<Node<SegmentData>>) => (
  <div className={`yy-pentest-segment-frame ${selected ? 'is-selected' : ''}`}>
    <NodeResizer isVisible={selected} minWidth={380} minHeight={260} />
    <Group justify="space-between" align="flex-start" wrap="nowrap">
      <Stack gap={2}>
        <Group gap={8} wrap="nowrap">
          <Badge size="sm" variant="light" className="yy-pentest-segment-badge">
            {zoneLabels[data.zoneType]}
          </Badge>
          <Text fw={900} className="yy-pentest-segment-title">
            {data.label}
          </Text>
        </Group>
        <Text className="yy-pentest-segment-meta">
          {data.slug} / {data.cidr}
        </Text>
      </Stack>
      <Badge variant="outline">{data.nodeCount} 节点</Badge>
    </Group>
  </div>
))

const AssetNode = memo(({ data, selected }: NodeProps<Node<AssetData>>) => (
  <div className={`yy-pentest-node-card ${selected ? 'is-selected' : ''}`}>
    <Handle type="target" position={Position.Left} className="yy-pentest-handle" />
    <Handle type="source" position={Position.Right} className="yy-pentest-handle" />
    <Group justify="space-between" gap={8} wrap="nowrap">
      <Text fw={900} className="yy-pentest-node-title">
        {data.label}
      </Text>
      <Badge size="xs" variant="light" className={`yy-pentest-node-type type-${data.nodeType.toLowerCase()}`}>
        {nodeTypeLabels[data.nodeType]}
      </Badge>
    </Group>
    <Text className="yy-pentest-node-template">{data.templateName}</Text>
    <div className="yy-pentest-node-grid">
      <span>{data.interfaceLabel}</span>
      <span>{data.isEntry ? '入口发布' : '内部访问'}</span>
      <span>{data.scoreLabel}</span>
    </div>
  </div>
))

const nodeTypes: NodeTypes = {
  pentestNetwork: SegmentNode,
  pentestAsset: AssetNode,
}

const PenetrationUsageGuide: FC = () => (
  <YinyuDrawerBody p="lg" className="yy-pentest-help-doc">
    <ScrollArea.Autosize mah="calc(100dvh - 8rem)">
      <Stack gap="lg">
        <Stack gap={6}>
          <Badge variant="light">低代码渗透场景编排</Badge>
          <Title order={3}>使用说明</Title>
          <Text className="yy-readable-text">
            渗透编排用于快速构建外网打点、多级内网、跳板横移和分段得分场景。平台负责队伍级网络隔离、IPAM、容器多网卡、入口端口、动态 Flag 与环境重置；镜像只需要提供具体服务能力。
          </Text>
        </Stack>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>推荐工作流</Title>
            <Text>1. 先点击“一键生成企业多级内网”，得到公网、DMZ、业务区、数据区和运维区的基础骨架。</Text>
            <Text>2. 从左侧拖入安全域或资产节点，节点拖入某个安全域后会自动绑定主网卡。</Text>
            <Text>3. 点击安全域配置名称、标识、样例 CIDR、默认策略和说明；未填写 CIDR 时由平台自动分配。</Text>
            <Text>4. 点击资产节点选择环境模板、服务端口、资源限制、网卡、环境变量和得分项。</Text>
            <Text>5. 在节点之间连线，表达访问路径、跳板关系或放行/拒绝策略，再到“计划”页查看 IPAM、端口、Flag 和运行期访问控制规则。</Text>
            <Text>6. 校验通过后依次保存、发布、部署；部署后可在“运行”页查看队伍环境、后台入口、提交日志、策略状态和重建操作。</Text>
          </Stack>
        </YinyuPanel>

        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <YinyuPanel p="md" className="yy-pentest-help-section">
            <Stack gap="xs">
              <Title order={4}>安全域</Title>
              <Text className="yy-readable-text">
                安全域代表网络和信任边界，例如公网、DMZ、业务区、数据区、运维区。每个安全域会为每支队伍生成独立 Docker 网络和独立 CIDR。
              </Text>
              <Text className="yy-readable-text">
                非公网且非入口安全域会创建为 Docker internal bridge，默认不能直接从外部访问，只能通过入口节点、跳板机、多网卡节点或管理员后台观测。
              </Text>
            </Stack>
          </YinyuPanel>

          <YinyuPanel p="md" className="yy-pentest-help-section">
            <Stack gap="xs">
              <Title order={4}>资产节点</Title>
              <Text className="yy-readable-text">
                节点代表真实资产角色，例如入口服务、Web 服务、数据库、跳板机、堡垒机、防火墙/路由和业务服务。节点角色主要用于场景表达，实际运行内容由环境模板或 Docker 镜像决定。
              </Text>
              <Text className="yy-readable-text">
                勾选“入口节点”或“发布宿主端口”后，平台会为该节点发布随机宿主端口。普通内网节点不发布端口。
              </Text>
            </Stack>
          </YinyuPanel>
        </SimpleGrid>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>网卡、IPAM 与多级内网</Title>
            <Text>每个节点至少需要一张网卡。主网卡决定节点默认所属安全域；额外网卡用于实现跳板机、防火墙/路由、堡垒机等跨网段资产。</Text>
            <Text>固定样例 IP 可以人工填写，平台会按队伍网段进行平移，保证所有队伍拓扑一致但彼此隔离。留空时平台自动分配。</Text>
            <Text>不要在 Dockerfile 或服务配置里写死队伍 IP。需要知道队伍或 Flag 时，读取平台注入的环境变量。</Text>
          </Stack>
        </YinyuPanel>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>Docker 模板要求</Title>
            <Text>环境模板直接复用平台现有环境模板。第一版渗透编排要求 Linux Docker 模板，且模板状态为 Ready。</Text>
            <Text>服务必须监听容器内配置的“服务端口”，通常监听 0.0.0.0。入口节点发布的是宿主随机端口，不需要镜像自己处理宿主端口。</Text>
            <Text>平台会创建 Docker bridge 网络、配置 IPAM、连接多网卡、注入动态 Flag、启动命令、健康检查和资源限制。</Text>
          </Stack>
        </YinyuPanel>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>选手侧与计分</Title>
            <Text>选手进入渗透工作台后，只能看到本队入口、可见拓扑、任务链、提交框和剩余重置次数。</Text>
            <Text>每个节点可以配置多个得分项，支持静态 Flag 和动态 Flag。动态 Flag 按比赛、队伍、节点、得分项和发布版本稳定生成，重置环境不会改变答案。</Text>
            <Text>选手重置会销毁并重建本队整套环境，消耗管理员配置的最大重置次数；管理员强制重建不消耗选手次数。</Text>
          </Stack>
        </YinyuPanel>
      </Stack>
    </ScrollArea.Autosize>
  </YinyuDrawerBody>
)

const getTemplateName = (node: PenetrationNodeModel, templates: ImageTemplateLite[]) =>
  templates.find((template) => template.id === node.imageTemplateId)?.name ??
  (node.imageName?.trim() ? node.imageName : '未绑定环境模板')

const toFlowNodes = (config: PenetrationConfigModel, templates: ImageTemplateLite[]): Node<SegmentData | AssetData>[] => {
  const nodeCountByNetwork = new Map<number, number>()
  config.nodes.forEach((node) => nodeCountByNetwork.set(node.networkId, (nodeCountByNetwork.get(node.networkId) ?? 0) + 1))
  const networks: Node<SegmentData>[] = config.networks.map((network, index) => ({
    id: flowNetworkId(network.id),
    type: 'pentestNetwork',
    position: { x: network.positionX || 80 + index * 700, y: network.positionY || 90 },
    data: {
      label: network.name,
      slug: network.slug,
      cidr: network.cidr || network.previewCidr || '自动分配',
      zoneType: network.zoneType,
      nodeCount: nodeCountByNetwork.get(network.id) ?? 0,
    },
    style: { width: network.width || NETWORK_W, height: network.height || NETWORK_H },
    className: 'yy-pentest-flow-network',
    draggable: true,
    selectable: true,
    zIndex: 0,
  }))
  const hosts: Node<AssetData>[] = config.nodes.map((node) => {
    const network = config.networks.find((item) => item.id === node.networkId) ?? config.networks[0]
    const totalScore = node.scoreItems.reduce((sum, item) => sum + item.score, 0)
    const primary = node.interfaces.find((item) => item.isPrimary) ?? node.interfaces[0]
    return {
      id: String(node.id),
      type: 'pentestAsset',
      parentId: flowNetworkId(network.id),
      extent: 'parent',
      position: { x: Math.max(28, node.positionX || 52), y: Math.max(76, node.positionY || 100) },
      data: {
        label: node.name,
        nodeType: node.nodeType,
        templateName: getTemplateName(node, templates),
        interfaceLabel: `${node.interfaces.length || 1} 网卡 / ${primary?.previewIp || primary?.staticIp || '自动 IP'}`,
        scoreLabel: `${node.scoreItems.length} 项 / ${totalScore} 分`,
        isEntry: node.isEntry || node.publishPort,
      },
      className: `yy-pentest-flow-node type-${node.nodeType.toLowerCase()}`,
      draggable: true,
      selectable: true,
      zIndex: 5,
    }
  })
  return [...networks, ...hosts]
}

const toFlowEdges = (config: PenetrationConfigModel): Edge[] =>
  config.edges
    .filter((edge) => edge.sourceNodeId && edge.targetNodeId)
    .map((edge) => ({
      id: String(edge.id || `${edge.sourceNodeId}-${edge.targetNodeId}`),
      source: String(edge.sourceNodeId),
      target: String(edge.targetNodeId),
      animated: edge.policyAction === PenetrationPolicyAction.Allow,
      label: edge.label || `${edge.protocol}/${edge.portRange}`,
      className: `yy-pentest-flow-edge action-${edge.policyAction.toLowerCase()}`,
    }))

const withFlowLayout = (config: PenetrationConfigModel, flowNodes: Node<SegmentData | AssetData>[]): PenetrationConfigModel =>
  normalizeConfig({
    ...config,
    networks: config.networks.map((network) => {
      const flow = flowNodes.find((item) => item.id === flowNetworkId(network.id))
      return flow
        ? {
            ...network,
            positionX: flow.position.x,
            positionY: flow.position.y,
            width: Number(flow.width ?? flow.style?.width) || network.width || NETWORK_W,
            height: Number(flow.height ?? flow.style?.height) || network.height || NETWORK_H,
          }
        : network
    }),
    nodes: config.nodes.map((node) => {
      const flow = flowNodes.find((item) => item.id === String(node.id))
      const parentId = flow?.parentId?.startsWith('network-') ? Number(flow.parentId.replace('network-', '')) : node.networkId
      return flow
        ? {
            ...node,
            networkId: Number.isFinite(parentId) ? parentId : node.networkId,
            positionX: flow.position.x,
            positionY: flow.position.y,
          }
        : node
    }),
  })

const findSavedByIndex = <T extends { id: number }>(before: T[], after: T[], id: number) => {
  const index = before.findIndex((item) => item.id === id)
  return index >= 0 ? after[index] : undefined
}

const remapSelectedTarget = (
  target: SelectedTarget,
  before: PenetrationConfigModel,
  after: PenetrationConfigModel
): SelectedTarget => {
  if (!target) return undefined

  if (target.kind === 'network') {
    if (after.networks.some((network) => network.id === target.id)) return target
    const source = before.networks.find((network) => network.id === target.id)
    const match =
      (source &&
        after.networks.find(
          (network) =>
            network.orderIndex === source.orderIndex &&
            (network.slug === source.slug || network.name === source.name)
        )) ||
      findSavedByIndex(before.networks, after.networks, target.id)
    return match ? { kind: 'network', id: match.id } : undefined
  }

  if (target.kind === 'node') {
    if (after.nodes.some((node) => node.id === target.id)) return target
    const source = before.nodes.find((node) => node.id === target.id)
    const match =
      (source &&
        after.nodes.find(
          (node) =>
            node.orderIndex === source.orderIndex &&
            node.name === source.name &&
            node.nodeType === source.nodeType
        )) ||
      findSavedByIndex(before.nodes, after.nodes, target.id)
    return match ? { kind: 'node', id: match.id } : undefined
  }

  if (after.edges.some((edge) => edge.id === target.id)) return target
  const source = before.edges.find((edge) => edge.id === target.id)
  const match =
    (source &&
      after.edges.find(
        (edge) =>
          edge.label === source.label &&
          edge.protocol === source.protocol &&
          edge.portRange === source.portRange &&
          edge.policyAction === source.policyAction
      )) ||
    findSavedByIndex(before.edges, after.edges, target.id)
  return match ? { kind: 'edge', id: match.id } : undefined
}

const BuilderInner: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const navigate = useNavigate()
  const { screenToFlowPosition } = useReactFlow()
  const [config, setConfig] = useState<PenetrationConfigModel>()
  const [templates, setTemplates] = useState<ImageTemplateLite[]>([])
  const templatesRef = useRef<ImageTemplateLite[]>([])
  const [plan, setPlan] = useState<PenetrationPlanModel>()
  const [environments, setEnvironments] = useState<PenetrationTeamEnvironmentModel[]>([])
  const [access, setAccess] = useState<PenetrationAdminAccessModel[]>([])
  const [submissions, setSubmissions] = useState<PenetrationSubmissionLogModel[]>([])
  const [selectedTarget, setSelectedTarget] = useState<SelectedTarget>()
  const [loading, setLoading] = useState(false)
  const [usageOpened, setUsageOpened] = useState(false)
  const [linkMode, setLinkMode] = useState(false)
  const [linkSourceNodeId, setLinkSourceNodeId] = useState<number>()
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
        disabled: !isReadyLinuxDockerTemplate(template),
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
      const [configRes, templateRes, planRes, envRes, accessRes, submissionRes] = await Promise.all([
        penetrationAdminApi.getConfig(gameId),
        fetch('/api/v1/image-templates').then((res) => res.json()),
        penetrationAdminApi.plan(gameId),
        penetrationAdminApi.getEnvironments(gameId),
        penetrationAdminApi.getAccess(gameId),
        penetrationAdminApi.getSubmissions(gameId, 30),
      ])
      const nextTemplates = (templateRes?.items ?? templateRes?.data ?? []) as ImageTemplateLite[]
      const nextConfig = normalizeConfig(configRes.data.networks.length ? configRes.data : fallbackConfig(gameId))
      templatesRef.current = nextTemplates
      setConfig(nextConfig)
      setTemplates(nextTemplates)
      setPlan(planRes.data)
      setEnvironments(envRes.data)
      setAccess(accessRes.data)
      setSubmissions(submissionRes.data.data ?? [])
      setSelectedTarget(nextConfig.networks[0] ? { kind: 'network', id: nextConfig.networks[0].id } : undefined)
      syncFlowWithTemplates(nextConfig)
    } catch (err) {
      showErrorMsg(err, (key) => key)
      const next = fallbackConfig(gameId)
      setConfig(next)
      syncFlowWithTemplates(next)
    } finally {
      setLoading(false)
    }
  }, [gameId, syncFlowWithTemplates])

  useEffect(() => {
    void load()
  }, [load])

  const updateConfig = (updater: (current: PenetrationConfigModel) => PenetrationConfigModel) => {
    setConfig((current) => {
      if (!current) return current
      const next = normalizeConfig(updater(withFlowLayout(current, nodes)))
      syncFlowWithTemplates(next)
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
      setSelectedTarget(remapSelectedTarget(selectedTarget, outgoing, saved))
      syncFlowWithTemplates(saved)
      const planRes = await penetrationAdminApi.plan(gameId)
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

  const runAction = async (kind: 'validate' | 'plan' | 'publish' | 'deploy' | 'stop') => {
    setLoading(true)
    try {
      if (kind !== 'stop') {
        const saved = await save(true, false)
        if (!saved) return
      }
      if (kind === 'validate' || kind === 'plan') {
        const res = await penetrationAdminApi.plan(gameId)
        setPlan(res.data)
        showNotification({
          color: res.data.validation.valid ? 'teal' : 'yellow',
          message: res.data.validation.valid ? '部署计划校验通过' : '部署计划存在问题',
          icon: <Icon path={mdiVectorLine} size={1} />,
        })
      } else if (kind === 'publish') {
        const res = await penetrationAdminApi.publish(gameId)
        setConfig(normalizeConfig(res.data))
        syncFlowWithTemplates(res.data)
        showNotification({ color: 'teal', message: '拓扑版本已发布', icon: <Icon path={mdiPublish} size={1} /> })
      } else if (kind === 'deploy') {
        const res = await penetrationAdminApi.deploy(gameId)
        showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiAccessPointNetwork} size={1} /> })
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
    const edge = makeEdge(newId(config.edges), source, target, '访问策略')
    const next = normalizeConfig({ ...config, edges: [...config.edges, edge] })
    setConfig(next)
    setSelectedTarget({ kind: 'edge', id: edge.id })
    setLinkMode(false)
    setLinkSourceNodeId(undefined)
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
    syncFlowWithTemplates(next)
  }

  const addNetwork = (zoneType = PenetrationZoneType.Custom, position?: { x: number; y: number }) => {
    if (!config) return
    const network = { ...defaultNetwork(newId(config.networks), config.networks.length, zoneType), ...(position ? { positionX: position.x, positionY: position.y } : {}) }
    const next = normalizeConfig({ ...config, networks: [...config.networks, network] })
    setConfig(next)
    setSelectedTarget({ kind: 'network', id: network.id })
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
    syncFlowWithTemplates(next)
  }

  const removeSelected = () => {
    if (!config || !selectedTarget) return
    updateConfig((current) => {
      if (selectedTarget.kind === 'network') {
        if (current.networks.length <= 1) return current
        const removedNodeIds = current.nodes.filter((node) => node.networkId === selectedTarget.id).map((node) => node.id)
        return {
          ...current,
          networks: current.networks.filter((network) => network.id !== selectedTarget.id),
          nodes: current.nodes.filter((node) => node.networkId !== selectedTarget.id),
          edges: current.edges.filter((edge) => !removedNodeIds.includes(edge.sourceNodeId) && !removedNodeIds.includes(edge.targetNodeId)),
        }
      }
      if (selectedTarget.kind === 'node') {
        return {
          ...current,
          nodes: current.nodes.filter((node) => node.id !== selectedTarget.id),
          edges: current.edges.filter((edge) => edge.sourceNodeId !== selectedTarget.id && edge.targetNodeId !== selectedTarget.id),
        }
      }
      return { ...current, edges: current.edges.filter((edge) => edge.id !== selectedTarget.id) }
    })
    setSelectedTarget(undefined)
  }

  const onDrop = (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    const payload = event.dataTransfer.getData('application/yinyu-pentest')
    if (!payload || !config) return
    const parsed = JSON.parse(payload) as { kind: 'network' | 'node'; value: string }
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

  const restartRuntimeNode = async (runtimeNodeId: number) => {
    setLoading(true)
    try {
      const res = await penetrationAdminApi.restartRuntimeNode(runtimeNodeId)
      showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiRefresh} size={1} /> })
      await load()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
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

  return (
    <div className="yy-pentest-fullscreen">
      <div className="yy-pentest-topbar">
        <Group gap="xs" wrap="nowrap">
          <Button variant="light" leftSection={<Icon path={mdiArrowLeft} size={0.85} />} onClick={() => navigate(`/admin/games/${gameId}/info`)}>
            退出编排
          </Button>
          <Badge variant="light">Penetration Builder</Badge>
          <YinyuStatusPill tone={config?.status === PenetrationDeploymentStatus.Running ? 'success' : 'neutral'}>
            {config?.status ?? 'Draft'} / v{config?.publishedVersion ?? 0}
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
          <Tooltip label="保存当前画布后，检查 CIDR、模板、IP、容量和部署预览">
            <Button variant="light" leftSection={<Icon path={mdiVectorLine} size={0.85} />} onClick={() => runAction('validate')}>
              校验/计划
            </Button>
          </Tooltip>
          <Tooltip label="校验通过后发布一个可部署的拓扑版本">
            <Button variant="light" leftSection={<Icon path={mdiPublish} size={0.85} />} onClick={() => runAction('publish')}>
              发布
            </Button>
          </Tooltip>
          <Tooltip label="按已发布版本为全部参赛队伍创建隔离网络和容器">
            <Button leftSection={<Icon path={mdiAccessPointNetwork} size={0.85} />} onClick={() => runAction('deploy')}>
              部署
            </Button>
          </Tooltip>
          <Tooltip label="停止并清理已部署的渗透环境">
            <Button color="red" variant="light" leftSection={<Icon path={mdiStop} size={0.85} />} onClick={() => runAction('stop')}>
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
        <div className="yy-pentest-studio">
          <YinyuPanel p="md" className="yy-pentest-toolbox">
            <ScrollArea.Autosize mah="calc(100dvh - 7.5rem)">
              <Stack gap="md">
                <Stack gap={4}>
                  <Title order={4}>场景工具箱</Title>
                  <Text size="sm" className="yy-readable-text">
                    拖拽安全域和资产到画布。安全域决定隔离网段，资产网卡决定真实连通关系。
                  </Text>
                </Stack>
                <div className="yy-pentest-flow-steps">
                  {['生成/拖拽拓扑', '配置模板与网卡', '连线表达访问路径', '保存并校验计划', '发布部署后观测'].map((step, index) => (
                    <div className="yy-pentest-flow-step" key={step}>
                      <b>{index + 1}</b>
                      <span>{step}</span>
                    </div>
                  ))}
                </div>
                <Text size="xs" className="yy-pentest-flow-note">
                  校验/计划会先保存当前画布，并预览每队 Docker 网络、网卡 IP、入口端口、访问控制规则和 Flag 注入结果。连线会解析为运行期主机侧访问策略，同时作为选手拓扑任务链。
                </Text>
                <Button fullWidth leftSection={<Icon path={mdiAutoFix} size={0.85} />} onClick={() => {
                  const next = buildEnterpriseBlueprint(gameId, templates, config)
                  setConfig(next)
                  setSelectedTarget({ kind: 'network', id: next.networks[0].id })
                  syncFlowWithTemplates(next)
                }}>
                  一键生成企业多级内网
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
                  添加访问策略/连线
                </Button>
                {linkMode ? (
                  <Text size="xs" className="yy-pentest-link-hint">
                    {linkSourceNodeId ? '请选择访问目标资产节点' : '请选择访问起点资产节点'}
                  </Text>
                ) : null}
                <Divider />
                <Text fw={900}>安全域</Text>
                <SimpleGrid cols={2}>
                  {Object.values(PenetrationZoneType).map((zone) => (
                    <button key={zone} type="button" className="yy-pentest-tool-chip" draggable onDragStart={(event) => dragStart(event, 'network', zone)} onClick={() => addNetwork(zone)}>
                      {zoneLabels[zone]}
                    </button>
                  ))}
                </SimpleGrid>
                <Text fw={900}>资产节点</Text>
                <SimpleGrid cols={2}>
                  {Object.values(PenetrationNodeType).map((type) => (
                    <button key={type} type="button" className="yy-pentest-tool-chip" draggable onDragStart={(event) => dragStart(event, 'node', type)} onClick={() => addNode(type)}>
                      {nodeTypeLabels[type]}
                    </button>
                  ))}
                </SimpleGrid>
                <Divider />
                <SimpleGrid cols={2}>
                  <NumberInput label="队伍网段" min={16} max={28} value={config.teamSubnetPrefix} onChange={(value) => updateConfig((current) => ({ ...current, teamSubnetPrefix: Number(value || 24) }))} />
                  <NumberInput label="安全域网段" min={24} max={30} value={config.networkSubnetPrefix} onChange={(value) => updateConfig((current) => ({ ...current, networkSubnetPrefix: Number(value || 28) }))} />
                </SimpleGrid>
                <TextInput label="地址池 CIDR" value={config.baseCidr} onChange={(event) => updateConfig((current) => ({ ...current, baseCidr: event.currentTarget.value }))} />
                <NumberInput label="选手最大重置次数" min={0} max={100} value={config.maxResetCount} onChange={(value) => updateConfig((current) => ({ ...current, maxResetCount: Number(value || 0) }))} />
              </Stack>
            </ScrollArea.Autosize>
          </YinyuPanel>

          <YinyuPanel className="yy-pentest-canvas yy-pentest-canvas-full" p={0}>
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

          <YinyuPanel p="md" className="yy-pentest-inspector yy-pentest-inspector-full">
            <ScrollArea.Autosize mah="calc(100dvh - 7.5rem)">
              <Tabs defaultValue="property" keepMounted={false}>
                <Tabs.List grow>
                  <Tabs.Tab value="property">属性</Tabs.Tab>
                  <Tabs.Tab value="plan">计划</Tabs.Tab>
                  <Tabs.Tab value="runtime">运行</Tabs.Tab>
                </Tabs.List>

                <Tabs.Panel value="property" pt="md">
                  {selectedNetwork ? (
                    <Stack gap="sm">
                      <Group justify="space-between">
                        <Title order={4}>安全域属性</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="名称" value={selectedNetwork.name} onChange={(event) => updateNetwork(selectedNetwork.id, { name: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <Select label="安全域类型" data={zoneOptions} value={selectedNetwork.zoneType} onChange={(value) => value && updateNetwork(selectedNetwork.id, { zoneType: value as PenetrationZoneType })} />
                        <TextInput label="节点数量" value={`${config.nodes.filter((node) => node.networkId === selectedNetwork.id).length} 个资产`} readOnly />
                      </SimpleGrid>
                      <SimpleGrid cols={2}>
                        <TextInput label="标识" value={selectedNetwork.slug} onChange={(event) => updateNetwork(selectedNetwork.id, { slug: event.currentTarget.value })} />
                        <TextInput label="样例 CIDR" value={selectedNetwork.cidr ?? ''} placeholder={selectedNetwork.previewCidr || '自动分配'} onChange={(event) => updateNetwork(selectedNetwork.id, { cidr: event.currentTarget.value || null })} />
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
                      <Select label="资产角色" data={nodeTypeOptions} value={selectedNode.nodeType} onChange={(value) => value && updateNode(selectedNode.id, { nodeType: value as PenetrationNodeType })} />
                      <Textarea label="场景说明" minRows={2} value={selectedNode.description ?? ''} onChange={(event) => updateNode(selectedNode.id, { description: event.currentTarget.value })} />
                      <Select label="环境模板" searchable clearable data={templateOptions} value={selectedNode.imageTemplateId ? String(selectedNode.imageTemplateId) : null} onChange={(value) => updateNode(selectedNode.id, { imageTemplateId: value ? Number(value) : null })} />
                      <TextInput label="备用 Docker 镜像" value={selectedNode.imageName ?? ''} onChange={(event) => updateNode(selectedNode.id, { imageName: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <NumberInput label="CPU(0.1 核)" min={1} value={selectedNode.cpuCount} onChange={(value) => updateNode(selectedNode.id, { cpuCount: Number(value || 1) })} />
                        <NumberInput label="内存 MB" min={64} value={selectedNode.memoryLimit} onChange={(value) => updateNode(selectedNode.id, { memoryLimit: Number(value || 64) })} />
                        <NumberInput label="存储 MB" min={64} value={selectedNode.storageLimit} onChange={(value) => updateNode(selectedNode.id, { storageLimit: Number(value || 64) })} />
                        <NumberInput label="服务端口" min={1} max={65535} value={selectedNode.exposePort} onChange={(value) => updateNode(selectedNode.id, { exposePort: Number(value || 80) })} />
                      </SimpleGrid>
                      <Group>
                        <Checkbox label="入口节点" checked={selectedNode.isEntry} onChange={(event) => updateNode(selectedNode.id, { isEntry: event.currentTarget.checked, publishPort: event.currentTarget.checked || selectedNode.publishPort })} />
                        <Checkbox label="发布宿主端口" checked={selectedNode.publishPort} onChange={(event) => updateNode(selectedNode.id, { publishPort: event.currentTarget.checked })} />
                      </Group>
                      <Divider label="网卡 / IPAM" />
                      {selectedNode.interfaces.map((item, index) => (
                        <YinyuPanel key={`${item.id}-${index}`} p="xs" className="yy-pentest-score-editor">
                          <SimpleGrid cols={2}>
                            <TextInput label="网卡名" value={item.name} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, name: event.currentTarget.value } : it) })} />
                            <Select label="所属安全域" data={config.networks.map((network) => ({ value: String(network.id), label: network.name }))} value={String(item.networkId)} onChange={(value) => value && updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, networkId: Number(value) } : it), networkId: item.isPrimary ? Number(value) : selectedNode.networkId })} />
                            <TextInput label="固定样例 IP" value={item.staticIp ?? ''} placeholder={item.previewIp || '自动分配'} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, staticIp: event.currentTarget.value } : it) })} />
                            <Group mt="1.6rem">
                              <Checkbox label="主网卡" checked={item.isPrimary} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => ({ ...it, isPrimary: it.id === item.id ? event.currentTarget.checked : false })) })} />
                              <Checkbox label="管理通道" checked={item.isManagement} onChange={(event) => updateNode(selectedNode.id, { interfaces: selectedNode.interfaces.map((it) => it.id === item.id ? { ...it, isManagement: event.currentTarget.checked } : it) })} />
                            </Group>
                          </SimpleGrid>
                        </YinyuPanel>
                      ))}
                      <Button variant="light" leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => updateNode(selectedNode.id, { interfaces: [...selectedNode.interfaces, { id: newId(selectedNode.interfaces), nodeId: selectedNode.id, networkId: selectedNode.networkId, name: `eth${selectedNode.interfaces.length}`, staticIp: '', isPrimary: false, isManagement: false, orderIndex: selectedNode.interfaces.length }] })}>
                        添加网卡
                      </Button>
                      <Divider label="得分项" />
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
                        <Title order={4}>访问策略</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="策略名称" value={selectedEdge.label ?? ''} onChange={(event) => updateEdge(selectedEdge.id, { label: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <Select label="协议" data={protocolOptions} value={selectedEdge.protocol} onChange={(value) => value && updateEdge(selectedEdge.id, { protocol: value as PenetrationProtocol })} />
                        <TextInput label="端口范围" value={selectedEdge.portRange} onChange={(event) => updateEdge(selectedEdge.id, { portRange: event.currentTarget.value })} />
                      </SimpleGrid>
                      <Select
                        label="动作"
                        data={[
                          { value: PenetrationPolicyAction.Allow, label: '允许访问' },
                          { value: PenetrationPolicyAction.Deny, label: '拒绝访问' },
                        ]}
                        value={selectedEdge.policyAction}
                        onChange={(value) => value && updateEdge(selectedEdge.id, { policyAction: value as PenetrationPolicyAction })}
                      />
                      <Checkbox label="作为路由/跳板提示" checked={selectedEdge.isRouteHint} onChange={(event) => updateEdge(selectedEdge.id, { isRouteHint: event.currentTarget.checked })} />
                      <Textarea label="说明" minRows={3} value={selectedEdge.description ?? ''} onChange={(event) => updateEdge(selectedEdge.id, { description: event.currentTarget.value })} />
                    </Stack>
                  ) : (
                    <Stack gap="xs" align="center" justify="center" mih={300}>
                      <Icon path={mdiRouterNetwork} size={2.2} />
                      <Text fw={900}>选择安全域、资产或连线</Text>
                      <Text className="yy-readable-text" ta="center">点击画布对象后，可在此配置网段、网卡、IP、访问策略和得分项。</Text>
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
                          <Group justify="space-between"><Text fw={900}>样例队伍网段</Text><Badge variant="light">{plan.sampleTeamPrefix}</Badge></Group>
                          <Text size="sm" className="yy-readable-text">参赛队伍：{plan.teamCount}，安全域：{plan.networks.length}，资产：{plan.nodes.length}</Text>
                          <Text size="sm" className="yy-readable-text">
                            访问控制：{plan.policyEnforcementMode}，样例队伍将生成 {plan.runtimePolicyRuleCount} 条运行期规则
                          </Text>
                        </YinyuPanel>
                        {[...(plan.validation.errors ?? []), ...(plan.validation.warnings ?? [])].map((message, index) => (
                          <Text key={`${message}-${index}`} className={plan.validation.errors.includes(message) ? 'yy-pentest-error' : 'yy-readable-text'} size="sm">
                            {message}
                          </Text>
                        ))}
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>安全域</Table.Th><Table.Th>CIDR</Table.Th><Table.Th>隔离</Table.Th><Table.Th>策略</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>{plan.networks.map((network) => <Table.Tr key={network.networkId}><Table.Td>{network.networkName}</Table.Td><Table.Td>{network.cidr}</Table.Td><Table.Td>{network.isInternal ? '内网隔离' : '入口可达'}</Table.Td><Table.Td>{network.defaultPolicy}</Table.Td></Table.Tr>)}</Table.Tbody>
                          </Table>
                        </YinyuTableShell>
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>资产</Table.Th><Table.Th>网卡/IP</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>{plan.nodes.map((node) => <Table.Tr key={node.nodeId}><Table.Td>{node.nodeName}</Table.Td><Table.Td>{node.interfaces.map((item) => `${item.name}:${item.ipAddress}${item.isInternal ? '(内网)' : ''}`).join(' / ')}</Table.Td></Table.Tr>)}</Table.Tbody>
                          </Table>
                        </YinyuTableShell>
                        <YinyuTableShell p="xs">
                          <Table>
                            <Table.Thead><Table.Tr><Table.Th>访问路径</Table.Th><Table.Th>来源</Table.Th><Table.Th>目标</Table.Th><Table.Th>协议/端口</Table.Th><Table.Th>运行期解析</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>
                              {plan.policies.length > 0 ? plan.policies.map((policy) => (
                                <Table.Tr key={policy.policyId}>
                                  <Table.Td>{policy.label}</Table.Td>
                                  <Table.Td>{policy.source}</Table.Td>
                                  <Table.Td>{policy.target}</Table.Td>
                                  <Table.Td>{policy.protocol.toUpperCase()} / {policy.portRange}</Table.Td>
                                  <Table.Td>
                                    <Stack gap={3}>
                                      {(policy.resolvedRules ?? []).slice(0, 4).map((rule) => (
                                        <Text size="xs" className="yy-readable-text" key={rule}>{rule}</Text>
                                      ))}
                                      {(policy.resolvedRules?.length ?? 0) > 4 && (
                                        <Text size="xs" className="yy-readable-text">另有 {(policy.resolvedRules?.length ?? 0) - 4} 条规则</Text>
                                      )}
                                    </Stack>
                                  </Table.Td>
                                </Table.Tr>
                              )) : (
                                <Table.Tr><Table.Td colSpan={5}>暂无访问路径。至少连接两个资产节点，用于表达任务链和跳板关系。</Table.Td></Table.Tr>
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
                  <Stack gap="sm">
                    <Group justify="space-between"><Title order={4}>运行观测</Title><Button size="xs" variant="light" onClick={load}>刷新</Button></Group>
                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead><Table.Tr><Table.Th>队伍</Table.Th><Table.Th>状态</Table.Th><Table.Th>节点</Table.Th><Table.Th>重置</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>{environments.map((env) => <Table.Tr key={env.environmentId}><Table.Td>{env.teamName}</Table.Td><Table.Td>{env.status}</Table.Td><Table.Td>{env.runtimeNodeCount}</Table.Td><Table.Td>{env.resetCount}/{config.maxResetCount}</Table.Td></Table.Tr>)}</Table.Tbody>
                      </Table>
                    </YinyuTableShell>
                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead><Table.Tr><Table.Th>后台访问</Table.Th><Table.Th>入口</Table.Th><Table.Th>操作</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>{access.slice(0, 12).map((item) => <Table.Tr key={item.runtimeNodeId}><Table.Td>{item.teamName} / {item.nodeName}</Table.Td><Table.Td>{item.url || item.internalIp}</Table.Td><Table.Td><Button size="xs" variant="light" onClick={() => restartRuntimeNode(item.runtimeNodeId)}>重建</Button></Table.Td></Table.Tr>)}</Table.Tbody>
                      </Table>
                    </YinyuTableShell>
                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead><Table.Tr><Table.Th>提交日志</Table.Th><Table.Th>得分项</Table.Th><Table.Th>状态</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>{submissions.slice(0, 12).map((item) => <Table.Tr key={item.id}><Table.Td>{item.teamName}</Table.Td><Table.Td>{item.nodeName} / {item.itemTitle}</Table.Td><Table.Td>{item.status}</Table.Td></Table.Tr>)}</Table.Tbody>
                      </Table>
                    </YinyuTableShell>
                  </Stack>
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
