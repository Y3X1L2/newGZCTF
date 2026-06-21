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
import {
  ImageTemplateLite,
  PenetrationAdminAccessModel,
  PenetrationConfigModel,
  PenetrationDefaultPolicy,
  PenetrationDeploymentEventLevel,
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

const runtimeStatusLabels: Record<PenetrationRuntimeStatus, string> = {
  [PenetrationRuntimeStatus.Pending]: '等待部署',
  [PenetrationRuntimeStatus.CreatingNetworks]: '创建网络',
  [PenetrationRuntimeStatus.CreatingContainers]: '创建容器',
  [PenetrationRuntimeStatus.Running]: '运行中',
  [PenetrationRuntimeStatus.Stopped]: '已停止',
  [PenetrationRuntimeStatus.Failed]: '部署失败',
  [PenetrationRuntimeStatus.CleanupPending]: '待清理',
  [PenetrationRuntimeStatus.Orphaned]: '资源孤儿',
  [PenetrationRuntimeStatus.ManualCleanupRequired]: '需人工清理',
}

const runtimeStatusTone = (status: PenetrationRuntimeStatus) => {
  if (status === PenetrationRuntimeStatus.Running) return 'success'
  if (status === PenetrationRuntimeStatus.Failed || status === PenetrationRuntimeStatus.ManualCleanupRequired) return 'danger'
  if (
    status === PenetrationRuntimeStatus.CreatingNetworks ||
    status === PenetrationRuntimeStatus.CreatingContainers ||
    status === PenetrationRuntimeStatus.CleanupPending ||
    status === PenetrationRuntimeStatus.Orphaned
  ) return 'warm'
  return 'neutral'
}

const runtimeStatusState = (status: PenetrationRuntimeStatus) => {
  if (status === PenetrationRuntimeStatus.Running) return 'running'
  if (status === PenetrationRuntimeStatus.Failed || status === PenetrationRuntimeStatus.ManualCleanupRequired) return 'alert'
  if (
    status === PenetrationRuntimeStatus.CreatingNetworks ||
    status === PenetrationRuntimeStatus.CreatingContainers ||
    status === PenetrationRuntimeStatus.CleanupPending ||
    status === PenetrationRuntimeStatus.Orphaned
  ) return 'busy'
  return 'idle'
}

const needsManualCleanup = (status: PenetrationRuntimeStatus) =>
  status === PenetrationRuntimeStatus.CleanupPending ||
  status === PenetrationRuntimeStatus.Orphaned ||
  status === PenetrationRuntimeStatus.ManualCleanupRequired

const deploymentEventTone = (level: PenetrationDeploymentEventLevel) => {
  if (level === PenetrationDeploymentEventLevel.Success) return 'success'
  if (level === PenetrationDeploymentEventLevel.Error) return 'danger'
  if (level === PenetrationDeploymentEventLevel.Warning) return 'warm'
  return 'neutral'
}

const deploymentEventLabel: Record<PenetrationDeploymentEventLevel, string> = {
  [PenetrationDeploymentEventLevel.Info]: '信息',
  [PenetrationDeploymentEventLevel.Success]: '成功',
  [PenetrationDeploymentEventLevel.Warning]: '警告',
  [PenetrationDeploymentEventLevel.Error]: '失败',
}

const enforcementLabels: Record<PenetrationEnforcementMode, string> = {
  [PenetrationEnforcementMode.HintOnly]: '仅题目提示',
  [PenetrationEnforcementMode.RuntimeRoute]: '运行期网络路由',
  [PenetrationEnforcementMode.Both]: '提示 + 运行期路由',
}

const edgeHintTitle = '协议/端口为题目提示字段'
const edgeHintText = '当前版本只执行网络级 fabric 隔离与显式路由，不执行端口级防火墙。这里填写的协议和端口会进入路径摘要、部署计划和选手提示。'

const routeStatusLabels: Record<PenetrationRouteStatus, string> = {
  [PenetrationRouteStatus.HintOnly]: '提示路径',
  [PenetrationRouteStatus.RoutePlanned]: '路由可部署',
  [PenetrationRouteStatus.RouteApplied]: '路由已应用',
  [PenetrationRouteStatus.RouteFailed]: '路由失败',
  [PenetrationRouteStatus.Unsupported]: '暂不支持',
}

const routeStatusTone = (status: PenetrationRouteStatus) => {
  if (status === PenetrationRouteStatus.RouteApplied || status === PenetrationRouteStatus.RoutePlanned) return 'success'
  if (status === PenetrationRouteStatus.RouteFailed || status === PenetrationRouteStatus.Unsupported) return 'danger'
  return 'neutral'
}

const formatDateTime = (value?: number | null) => value
  ? new Date(value).toLocaleString()
  : '-'

const parseRuntimeInterfaces = (summary?: string | null): { interfaceName?: string; networkName?: string; ipAddress?: string; cidr?: string; isPrimary?: boolean }[] => {
  if (!summary) return []
  try {
    const parsed = JSON.parse(summary)
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

const shortText = (value?: string | null, length = 12) => {
  if (!value) return '-'
  return value.length <= length ? value : value.slice(0, length)
}

const zoneOptions = Object.values(PenetrationZoneType).map((value) => ({ value, label: zoneLabels[value] }))
const nodeTypeOptions = Object.values(PenetrationNodeType).map((value) => ({ value, label: nodeTypeLabels[value] }))
const protocolOptions = Object.values(PenetrationProtocol).map((value) => ({ value, label: value.toUpperCase() }))
const enforcementOptions = Object.values(PenetrationEnforcementMode).map((value) => ({ value, label: enforcementLabels[value] }))

const flowNetworkId = (id: number) => `network-${id}`
// 临时 ID 递减计数器，确保在 Int32 范围内且全局唯一
let tempIdSeq = 0
const nextTempId = () => --tempIdSeq
const newId = (items: { id: number }[]) => Math.min(-1, ...items.map((item) => item.id)) - 1
const newTopologyKey = (prefix: string, id?: number) =>
  `${prefix}-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}-${Math.abs(id ?? 0)}`
const enumKey = (value: string | number | undefined | null) => String(value ?? '').toLowerCase()
const isReadyLinuxDockerTemplate = (template: ImageTemplateLite) =>
  (enumKey(template.osType) === '0' || enumKey(template.osType) === 'linux') &&
  (enumKey(template.imageType) === '0' || enumKey(template.imageType) === 'docker') &&
  (enumKey(template.status) === '0' || enumKey(template.status) === 'ready')
const normalizeConfig = (config: PenetrationConfigModel): PenetrationConfigModel => {
  const networks = config.networks.map((network) => ({
    ...network,
    topologyKey: network.topologyKey || newTopologyKey('network', network.id),
  }))
  const nodes = config.nodes.map((node) => ({
    ...node,
    topologyKey: node.topologyKey || newTopologyKey('node', node.id),
    playerAlias: node.playerAlias ?? '',
    playerDescription: node.playerDescription ?? '',
    allowRouting:
      node.allowRouting ||
      node.nodeType === PenetrationNodeType.JumpHost ||
      node.nodeType === PenetrationNodeType.Bastion ||
      node.nodeType === PenetrationNodeType.FirewallRouter,
    interfaces: node.interfaces.map((item) => ({
      ...item,
      topologyKey: item.topologyKey || newTopologyKey('interface', item.id),
      nodeId: node.id,
    })),
    scoreItems: node.scoreItems.map((item) => ({
      ...item,
      topologyKey: item.topologyKey || newTopologyKey('score', item.id),
      isCheckpoint: item.isCheckpoint ?? false,
    })),
  }))

  return {
    ...config,
    networks,
    nodes,
    interfaces: nodes.flatMap((node) => node.interfaces.map((item) => ({ ...item, nodeId: node.id }))),
    edges: config.edges.map((edge) => ({
      ...edge,
      topologyKey: edge.topologyKey || newTopologyKey('edge', edge.id),
      enforcementMode: edge.enforcementMode ?? (edge.isRouteHint ? PenetrationEnforcementMode.HintOnly : PenetrationEnforcementMode.HintOnly),
      priority: edge.priority ?? 100,
    })),
  }
}

const defaultNetwork = (id: number, orderIndex: number, zoneType = PenetrationZoneType.Custom): PenetrationNetworkModel => ({
  id,
  topologyKey: newTopologyKey('network', id),
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
  id: nextTempId(),
  topologyKey: newTopologyKey('score'),
  title: `得分项 ${orderIndex + 1}`,
  description: '',
  category: '综合',
  score: 100,
  isDynamic: true,
  staticFlag: '',
  flagTemplate: 'flag{[TEAM_HASH]}',
  maxAttempts: 0,
  isVisible: true,
  isCheckpoint: orderIndex === 0,
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
    topologyKey: newTopologyKey('node', id),
    networkId: network.id,
    name: isEntry ? '外网入口服务' : nodeTypeLabels[nodeType],
    description: '',
    playerAlias: isEntry ? '入口目标' : '',
    playerDescription: '',
    nodeType,
    imageTemplateId: null,
    imageName: '',
    cpuCount: 10,
    memoryLimit: 512,
    storageLimit: 512,
    exposePort: isEntry ? 8080 : 80,
    isEntry,
    publishPort: isEntry,
    allowRouting:
      nodeType === PenetrationNodeType.JumpHost ||
      nodeType === PenetrationNodeType.Bastion ||
      nodeType === PenetrationNodeType.FirewallRouter,
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
        id: nextTempId(),
        topologyKey: newTopologyKey('interface'),
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
    baseCidr: '10.60.0.0/16',
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
  const readyTemplates = templates.filter(isReadyLinuxDockerTemplate)
  const findTemplate = (serviceKey: string) => {
    const normalized = serviceKey.toLowerCase()
    const compact = normalized.replaceAll('-', '')
    return readyTemplates.find((template) => {
      const haystack = `${template.name ?? ''} ${template.registryUrl ?? ''}`.toLowerCase()
      return haystack.includes(normalized) || haystack.replaceAll('-', '').includes(compact)
    })
  }
  const networkSpecs = [
    {
      id: -101,
      key: 'nm-net-public-edge',
      zone: PenetrationZoneType.Public,
      name: 'Public / Edge 入口区',
      slug: 'public-edge',
      cidr: '10.60.0.0/28',
      trust: 10,
      x: 60,
      y: 130,
      w: 560,
      h: 320,
      description: '选手唯一入口安全域。只发布 edge-gateway，选手获得节点地址后自行扫描入口服务。',
    },
    {
      id: -102,
      key: 'nm-net-dmz-service',
      zone: PenetrationZoneType.Dmz,
      name: 'DMZ / 对外业务区',
      slug: 'dmz-service',
      cidr: '10.60.0.16/28',
      trust: 30,
      x: 700,
      y: 80,
      w: 620,
      h: 430,
      description: '企业官网、资源中心和客户上传中心，承接公开入口后的第一层业务服务。',
    },
    {
      id: -103,
      key: 'nm-net-biz-core',
      zone: PenetrationZoneType.Business,
      name: 'Business / AI 业务核心区',
      slug: 'biz-core',
      cidr: '10.60.0.32/28',
      trust: 55,
      x: 1420,
      y: 120,
      w: 680,
      h: 460,
      description: '文档解析、AI 控制台 API 和任务队列所在的业务内网。',
    },
    {
      id: -104,
      key: 'nm-net-data-plane',
      zone: PenetrationZoneType.Data,
      name: 'Data / 数据与模型区',
      slug: 'data-plane',
      cidr: '10.60.0.48/28',
      trust: 85,
      x: 2220,
      y: 70,
      w: 720,
      h: 520,
      description: '客户数据库、对象存储、密钥服务和模型仓库，承载高分与终局线索。',
    },
    {
      id: -105,
      key: 'nm-net-ops-control',
      zone: PenetrationZoneType.Operations,
      name: 'Operations / 运维控制区',
      slug: 'ops-control',
      cidr: '10.60.0.64/28',
      trust: 70,
      x: 1450,
      y: 690,
      w: 670,
      h: 360,
      description: '内部 Git 与 CI Runner，串联业务配置泄露、构建变量和后续数据区突破。',
    },
  ]

  const networks = networkSpecs.map((spec, index) => ({
    ...defaultNetwork(spec.id, index, spec.zone),
    topologyKey: spec.key,
    name: spec.name,
    slug: spec.slug,
    cidr: spec.cidr,
    trustLevel: spec.trust,
    description: spec.description,
    defaultPolicy: PenetrationDefaultPolicy.DenyAll,
    isEntry: spec.zone === PenetrationZoneType.Public,
    positionX: spec.x,
    positionY: spec.y,
    width: spec.w,
    height: spec.h,
  }))
  const networkBySlug = Object.fromEntries(networks.map((network) => [network.slug, network]))

  const score = (
    id: number,
    title: string,
    category: string,
    scoreValue: number,
    description: string,
    orderIndex: number,
    checkpoint = true
  ): PenetrationScoreItemModel => ({
    ...defaultScoreItem(orderIndex),
    id,
    topologyKey: `nm-score-${title.toLowerCase().replaceAll('_', '-')}`,
    title,
    category,
    score: scoreValue,
    description,
    flagTemplate: 'flag{[TEAM_HASH]}',
    isCheckpoint: checkpoint,
    orderIndex,
  })

  type InterfaceSpec = {
    net: string
    name?: string
    primary?: boolean
    management?: boolean
  }
  type NodeSpec = {
    id: number
    key: string
    serviceKey: string
    name: string
    alias: string
    description: string
    playerDescription?: string
    type: PenetrationNodeType
    exposePort: number
    cpu: number
    memory: number
    storage: number
    x: number
    y: number
    entry?: boolean
    publish?: boolean
    routing?: boolean
    interfaces: InterfaceSpec[]
    env?: Record<string, string>
    scores: PenetrationScoreItemModel[]
  }

  const url = (key: string, port?: number) => port ? `{{node:nm-node-${key}:url:${port}}}` : `{{node:nm-node-${key}:url}}`
  const host = (key: string) => `{{node:nm-node-${key}:host}}`

  const nodeSpecs: NodeSpec[] = [
    {
      id: -201,
      key: 'edge-gateway',
      serviceKey: 'edge-gateway',
      name: 'edge-gateway 外部入口网关',
      alias: '入口目标',
      description: 'NebulaMind 唯一对选手发布的入口网关。公开 80 端口，反向代理企业官网和客户支持入口；/status/build-info 承载外网发现低分 Flag。',
      playerDescription: '你获得了 NebulaMind AI Corp 的授权测试入口地址。请从公开服务开始评估，逐步获取内网数据库与模型仓库中的敏感标识。',
      type: PenetrationNodeType.Entry,
      exposePort: 80,
      cpu: 3,
      memory: 128,
      storage: 256,
      x: 70,
      y: 115,
      entry: true,
      publish: true,
      routing: true,
      interfaces: [
        { net: 'public-edge', name: 'eth0', primary: true, management: true },
        { net: 'dmz-service', name: 'eth1' },
      ],
      env: {
        NM_PORTAL_WEB_URL: url('portal-web'),
        NM_SUPPORT_UPLOAD_URL: url('support-upload'),
      },
      scores: [
        score(-601, 'PUBLIC_DISCOVERY', 'A 外网发现', 50, '扫描入口服务，访问构建信息或镜像站泄露的 build metadata，获取公开发现 Flag。', 0),
      ],
    },
    {
      id: -202,
      key: 'portal-web',
      serviceKey: 'portal-web',
      name: 'portal-web 企业官网与资源中心',
      alias: '题目 02',
      description: '真实企业门户、产品页、客户案例、资源中心和遗留静态资源。包含隐藏目录与 Source Map 泄露。',
      type: PenetrationNodeType.Web,
      exposePort: 8080,
      cpu: 5,
      memory: 256,
      storage: 512,
      x: 70,
      y: 96,
      interfaces: [{ net: 'dmz-service', primary: true }],
      env: {
        NM_AI_CONSOLE_API_URL: url('ai-console-api'),
        NM_AI_CONSOLE_API_HOST: host('ai-console-api'),
      },
      scores: [
        score(-602, 'PORTAL_HIDDEN_DOCS', 'A 外网发现', 80, '从 robots.txt 和资源归档目录发现旧版白皮书/导出页面中的内部跟踪标识。', 0),
        score(-603, 'PORTAL_SOURCEMAP', 'A 外网发现', 120, '分析生产 Source Map，发现控制台 API、测试租户和调试 Flag。', 1),
      ],
    },
    {
      id: -203,
      key: 'support-upload',
      serviceKey: 'support-upload',
      name: 'support-upload 客户支持上传中心',
      alias: '题目 03',
      description: '客户工单与日志包上传中心。通过上传绕过、路径穿越和 worker 配置泄露进入业务区。',
      type: PenetrationNodeType.Web,
      exposePort: 8080,
      cpu: 5,
      memory: 256,
      storage: 512,
      x: 330,
      y: 236,
      routing: true,
      interfaces: [
        { net: 'dmz-service', primary: true },
        { net: 'biz-core', name: 'eth1' },
      ],
      env: {
        NM_DOCUMENT_WORKER_URL: url('document-worker'),
        NM_DOCUMENT_WORKER_HOST: host('document-worker'),
        NM_CACHE_BROKER_HOST: host('cache-broker'),
        NM_AI_CONSOLE_API_URL: url('ai-console-api'),
      },
      scores: [
        score(-604, 'UPLOAD_MIME_BYPASS', 'B DMZ 利用', 150, '绕过日志包 MIME/扩展名校验，读取解析任务中的工单 Flag。', 0),
        score(-605, 'UPLOAD_PATH_TRAVERSAL', 'B DMZ 利用', 180, '利用下载接口路径穿越读取 worker.yml，获得业务区地址、队列和后续 token 线索。', 1),
      ],
    },
    {
      id: -204,
      key: 'document-worker',
      serviceKey: 'document-worker',
      name: 'document-worker 文档解析 Worker',
      alias: '题目 04',
      description: '异步文档解析与 URL 抓取 Worker。承接上传中心泄露 token，触发 SSRF 与命令注入。',
      type: PenetrationNodeType.Service,
      exposePort: 8080,
      cpu: 8,
      memory: 512,
      storage: 512,
      x: 70,
      y: 115,
      interfaces: [{ net: 'biz-core', primary: true }],
      env: {
        NM_DOCUMENT_WORKER_URL: url('document-worker'),
        NM_DOCUMENT_WORKER_HOST: host('document-worker'),
        NM_CACHE_BROKER_HOST: host('cache-broker'),
        NM_AI_CONSOLE_API_URL: url('ai-console-api'),
        NM_AI_CONSOLE_API_HOST: host('ai-console-api'),
      },
      scores: [
        score(-606, 'WORKER_SSRF_METADATA', 'B DMZ 利用', 220, '使用上传中心泄露的 worker token 触发 SSRF，读取控制台 metadata 响应中的 Flag。', 0),
        score(-607, 'WORKER_COMMAND_INJECTION', 'D 异步任务', 320, '通过队列或解析参数注入命令，读取 Worker 内部受限 Flag 文件。', 1),
      ],
    },
    {
      id: -205,
      key: 'ai-console-api',
      serviceKey: 'ai-console-api',
      name: 'ai-console-api AI 控制台 API',
      alias: '题目 05',
      description: 'AI 知识库、租户、审计与集成密钥 API。作为业务区进入数据区和运维区的关键边界服务。',
      type: PenetrationNodeType.Service,
      exposePort: 8080,
      cpu: 10,
      memory: 512,
      storage: 512,
      x: 310,
      y: 150,
      routing: true,
      interfaces: [
        { net: 'biz-core', primary: true },
        { net: 'data-plane', name: 'eth1' },
        { net: 'ops-control', name: 'eth2' },
      ],
      env: {
        NM_GIT_SERVICE_URL: url('git-service'),
        NM_OBJECT_STORE_URL: url('object-store', 9000),
      },
      scores: [
        score(-608, 'API_TENANT_IDOR', 'C 身份越权', 180, '利用 sourcemap 中的测试租户 ID 枚举废弃知识库，读取越权描述字段。', 0),
        score(-609, 'API_JWT_ROLE', 'C 身份越权', 240, '复用 Worker 泄露的 JWT 弱密钥伪造 operator 角色，导出审计日志。', 1),
        score(-610, 'API_GRAPHQL_AUDIT', 'C 身份越权', 260, '利用 GraphQL introspection 和 operator token 查询集成密钥，获得对象存储与 Git 线索。', 2),
      ],
    },
    {
      id: -206,
      key: 'cache-broker',
      serviceKey: 'cache-broker',
      name: 'cache-broker 任务缓存与消息队列',
      alias: '题目 06',
      description: '业务区 Redis 队列与任务结果缓存。无认证访问暴露解析结果和后续队列注入路径。',
      type: PenetrationNodeType.Service,
      exposePort: 6379,
      cpu: 3,
      memory: 128,
      storage: 256,
      x: 550,
      y: 285,
      interfaces: [{ net: 'biz-core', primary: true }],
      env: {
        NM_CACHE_BROKER_HOST: host('cache-broker'),
      },
      scores: [
        score(-611, 'REDIS_QUEUE_INFO', 'D 异步任务', 220, '连接未鉴权 Redis，读取 task:result:* 中的任务结果和内部服务地址。', 0),
      ],
    },
    {
      id: -207,
      key: 'git-service',
      serviceKey: 'git-service',
      name: 'git-service 内部 Git 服务',
      alias: '题目 07',
      description: '轻量内部 Git/代码浏览服务，包含历史提交、配置样例和运维文档泄露。',
      type: PenetrationNodeType.Service,
      exposePort: 3000,
      cpu: 10,
      memory: 512,
      storage: 768,
      x: 72,
      y: 90,
      interfaces: [{ net: 'ops-control', primary: true }],
      env: {
        NM_GIT_SERVICE_URL: url('git-service'),
        NM_CUSTOMER_DB_HOST: host('customer-db'),
        NM_OBJECT_STORE_URL: url('object-store', 9000),
        NM_CACHE_BROKER_HOST: host('cache-broker'),
        NM_AI_CONSOLE_API_URL: url('ai-console-api'),
        NM_DOCUMENT_WORKER_URL: url('document-worker'),
        NM_PORTAL_WEB_URL: url('portal-web'),
      },
      scores: [
        score(-612, 'GIT_CONFIG_SECRET', 'E Git / CI', 180, '克隆内部仓库并查看历史提交，恢复旧配置文件中的 Flag 和 CI 项目线索。', 0),
      ],
    },
    {
      id: -208,
      key: 'ci-runner',
      serviceKey: 'ci-runner',
      name: 'ci-runner CI 构建执行节点',
      alias: '题目 08',
      description: '内部 CI 项目变量、构建触发与 runner 执行环境。串联 Vault、数据库和对象存储凭据。',
      type: PenetrationNodeType.Service,
      exposePort: 8080,
      cpu: 10,
      memory: 512,
      storage: 768,
      x: 330,
      y: 185,
      routing: true,
      interfaces: [
        { net: 'ops-control', primary: true },
        { net: 'data-plane', name: 'eth1' },
      ],
      env: {
        NM_GIT_SERVICE_URL: url('git-service'),
        NM_CUSTOMER_DB_HOST: host('customer-db'),
        NM_CACHE_BROKER_HOST: host('cache-broker'),
        NM_SECRETS_VAULT_URL: url('secrets-vault', 8200),
      },
      scores: [
        score(-613, 'CI_VARIABLE_LEAK', 'E Git / CI', 260, '利用项目 token 读取未正确脱敏的 CI 变量，获得对象存储 admin key 与 Vault token。', 0),
        score(-614, 'CI_RUNNER_EXEC', 'E Git / CI', 380, '触发构建脚本注入，在 runner 日志中回显受限文件内容。', 1),
      ],
    },
    {
      id: -209,
      key: 'customer-db',
      serviceKey: 'customer-db',
      name: 'customer-db 客户与标注数据库',
      alias: '题目 09',
      description: 'PostgreSQL 客户、合同、标注、审计和受监管模型训练记录。终局链路落点在核心客户训练数据审计记录。',
      type: PenetrationNodeType.Database,
      exposePort: 5432,
      cpu: 10,
      memory: 768,
      storage: 1024,
      x: 74,
      y: 96,
      interfaces: [{ net: 'data-plane', primary: true }],
      env: {
        NM_DB_ADMIN_PASSWORD: 'nm_admin_dev_2026',
      },
      scores: [
        score(-615, 'DB_READONLY_CUSTOMERS', 'F 数据库', 260, '复用泄露的 readonly 凭据查询 security_findings，获得客户数据访问审计 Flag。', 0),
        score(-616, 'DB_PRIVESC_FUNCTION', 'F 数据库', 360, '利用 SECURITY DEFINER 函数越权读取 internal_exports 中的内部审计快照。', 1),
        score(-617, 'DB_CORE_CUSTOMER_DATA', 'F 数据库 / 终局', 420, '使用高权限数据库凭据读取 regulated_model_training_records 中的核心客户训练数据审计标记；该记录也是模型供应链终局链路的收束点。', 2),
      ],
    },
    {
      id: -210,
      key: 'object-store',
      serviceKey: 'object-store',
      name: 'object-store 对象存储与模型资源',
      alias: '题目 10',
      description: 'MinIO 对象存储，包含公开模型资源、训练日志、导出 CSV 和模型 manifest 线索。',
      type: PenetrationNodeType.Service,
      exposePort: 9000,
      cpu: 8,
      memory: 512,
      storage: 1024,
      x: 330,
      y: 95,
      interfaces: [{ net: 'data-plane', primary: true }],
      env: {
        NM_CUSTOMER_DB_HOST: host('customer-db'),
        NM_MODEL_REGISTRY_URL: url('model-registry'),
        NM_OBJECT_STORE_URL: url('object-store', 9000),
      },
      scores: [
        score(-618, 'OBJECT_BUCKET_POLICY', 'D 对象存储', 240, '利用低权限 access key 列出误公开 bucket，下载 CSV 并发现数据表与 Flag。', 0),
      ],
    },
    {
      id: -211,
      key: 'secrets-vault',
      serviceKey: 'secrets-vault',
      name: 'secrets-vault 密钥配置服务',
      alias: '题目 11',
      description: 'Vault mock 密钥服务。Bootstrap Token 滥用可读取模型仓库 token、数据库 admin 凭据和对象存储线索。',
      type: PenetrationNodeType.Service,
      exposePort: 8200,
      cpu: 5,
      memory: 256,
      storage: 512,
      x: 74,
      y: 315,
      interfaces: [{ net: 'data-plane', primary: true }],
      env: {
        NM_CUSTOMER_DB_HOST: host('customer-db'),
        NM_MODEL_REGISTRY_URL: url('model-registry'),
        NM_CI_RUNNER_URL: url('ci-runner'),
        NM_OBJECT_STORE_URL: url('object-store', 9000),
        NM_OBJECT_STORE_CONSOLE_URL: url('object-store', 9001),
      },
      scores: [
        score(-619, 'VAULT_POLICY_BYPASS', 'G 密钥服务', 360, '使用 CI 变量泄露的 bootstrap token 读取 model-registry secret，获得模型仓库管理员 token。', 0),
      ],
    },
    {
      id: -212,
      key: 'model-registry',
      serviceKey: 'model-registry',
      name: 'model-registry 模型仓库',
      alias: '题目 12',
      description: '模型版本、manifest、训练配置和供应链审计线索。G2 Flag 位于私有模型 manifest，终局线索指向对象存储训练日志和 customer-db 审计记录。',
      type: PenetrationNodeType.Service,
      exposePort: 8080,
      cpu: 5,
      memory: 256,
      storage: 512,
      x: 380,
      y: 315,
      interfaces: [{ net: 'data-plane', primary: true }],
      env: {
        NM_MODEL_REGISTRY_URL: url('model-registry'),
        NM_CUSTOMER_DB_HOST: host('customer-db'),
        NM_OBJECT_STORE_URL: url('object-store', 9000),
      },
      scores: [
        score(-620, 'MODEL_REGISTRY_ADMIN', 'G 模型仓库', 420, '使用 Vault 中的模型仓库管理员 token 下载 recommendation-v4-private v4 manifest，读取高分 Flag 与终局线索。', 0),
      ],
    },
  ]

  const nodes = nodeSpecs.map((spec, index): PenetrationNodeModel => {
    const primaryInterface = spec.interfaces.find((item) => item.primary) ?? spec.interfaces[0]
    const primaryNetwork = networkBySlug[primaryInterface.net]
    const template = findTemplate(spec.serviceKey)
    return {
      ...defaultNode(spec.id, primaryNetwork, index, spec.type),
      topologyKey: `nm-node-${spec.key}`,
      name: spec.name,
      description: spec.description,
      playerAlias: spec.alias,
      playerDescription: spec.playerDescription ?? '',
      imageTemplateId: template?.id ?? null,
      imageName: template ? '' : `nebulamind/${spec.serviceKey}:local`,
      cpuCount: spec.cpu,
      memoryLimit: spec.memory,
      storageLimit: spec.storage,
      exposePort: spec.exposePort,
      isEntry: spec.entry ?? false,
      publishPort: spec.publish ?? false,
      allowRouting: spec.routing ?? false,
      staticIp: '',
      environmentVariables: spec.env ?? {},
      startCommand: '',
      healthCheck: '',
      positionX: spec.x,
      positionY: spec.y,
      orderIndex: index,
      interfaces: spec.interfaces.map((item, ifaceIndex) => ({
        id: -801 - index * 10 - ifaceIndex,
        topologyKey: `nm-if-${spec.key}-${item.name ?? `eth${ifaceIndex}`}`,
        nodeId: spec.id,
        networkId: networkBySlug[item.net].id,
        name: item.name ?? `eth${ifaceIndex}`,
        staticIp: '',
        previewIp: '',
        isPrimary: item.primary ?? ifaceIndex === 0,
        isManagement: item.management ?? false,
        orderIndex: ifaceIndex,
      })),
      scoreItems: spec.scores,
    }
  })
  const nodeByKey = Object.fromEntries(nodes.map((node) => [node.topologyKey, node]))
  const edge = (id: number, sourceKey: string, targetKey: string, label: string, description: string, priority: number) => ({
    ...makeEdge(id, nodeByKey[`nm-node-${sourceKey}`], nodeByKey[`nm-node-${targetKey}`], label, 'any'),
    topologyKey: `nm-edge-${sourceKey}-to-${targetKey}`,
    enforcementMode: PenetrationEnforcementMode.HintOnly,
    isRouteHint: true,
    priority,
    description,
  })
  const edges: PenetrationEdgeModel[] = [
    edge(-301, 'edge-gateway', 'portal-web', '入口网关到企业官网', 'Edge 网关将公开入口流量代理到 DMZ 企业官网。', 10),
    edge(-302, 'edge-gateway', 'support-upload', '入口网关到客户上传中心', 'Edge 网关将 /support/ 路径代理到客户支持上传中心。', 20),
    edge(-303, 'support-upload', 'document-worker', '上传中心到文档 Worker', '路径穿越泄露 worker 配置后，上传中心业务链路进入文档解析 Worker。', 30),
    edge(-304, 'document-worker', 'cache-broker', '文档 Worker 到任务队列', 'Worker 使用 Redis 任务缓存，队列结果暴露内部线索。', 40),
    edge(-305, 'document-worker', 'ai-console-api', '文档 Worker 到 AI 控制台 API', 'SSRF 与 service-account 线索推动选手进入业务控制台。', 50),
    edge(-306, 'ai-console-api', 'object-store', 'AI 控制台到对象存储', 'GraphQL 集成密钥泄露对象存储 endpoint 和低权限 key。', 60),
    edge(-307, 'ai-console-api', 'git-service', 'AI 控制台到内部 Git', '审计导出暴露 Git 服务地址和项目线索。', 70),
    edge(-308, 'git-service', 'ci-runner', '内部 Git 到 CI Runner', 'Git 历史泄露 CI 项目名和 token，进入构建系统。', 80),
    edge(-309, 'ci-runner', 'secrets-vault', 'CI Runner 到 Vault', 'CI 变量泄露 bootstrap token，进入密钥配置服务。', 90),
    edge(-310, 'ci-runner', 'customer-db', 'CI Runner 到客户数据库', 'CI 变量和配置样例泄露数据库只读与高权凭据。', 100),
    edge(-311, 'secrets-vault', 'model-registry', 'Vault 到模型仓库', 'Vault secret 提供模型仓库 admin token，打开私有模型 manifest。', 110),
    edge(-312, 'model-registry', 'object-store', '模型仓库到训练日志', '私有模型 manifest 指向对象存储中的训练日志与审计记录。', 120),
    edge(-313, 'object-store', 'customer-db', '对象存储到核心数据库', '训练日志和 CSV 线索收束到 customer-db 受监管模型训练记录。', 130),
  ]
  return normalizeConfig({
    ...(current ?? fallbackConfig(gameId)),
    gameId,
    baseCidr: '10.60.0.0/16',
    teamSubnetPrefix: 24,
    networkSubnetPrefix: 28,
    maxResetCount: current?.maxResetCount ?? 3,
    networks,
    nodes,
    edges,
  })
}

const makeEdge = (id: number, source: PenetrationNodeModel, target: PenetrationNodeModel, label: string, portRange = 'any'): PenetrationEdgeModel => ({
  id,
  topologyKey: newTopologyKey('edge', id),
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
  enforcementMode: PenetrationEnforcementMode.HintOnly,
  priority: 100,
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
            <Text>5. 在节点之间连线，表达访问路径、跳板关系或放行策略，再到“计划”页校验 IPAM 和部署结果。</Text>
            <Text>6. 校验通过后依次保存、发布、部署；部署后可在“运行”页查看队伍环境、后台入口、提交日志和重建操作。</Text>
          </Stack>
        </YinyuPanel>

        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <YinyuPanel p="md" className="yy-pentest-help-section">
            <Stack gap="xs">
              <Title order={4}>安全域</Title>
              <Text className="yy-readable-text">
                安全域代表网络和信任边界，例如公网、DMZ、业务区、数据区、运维区。每个安全域会为每支队伍生成独立 CIDR 和平台管理的 fabric 网络。
              </Text>
              <Text className="yy-readable-text">
                普通内网节点默认不挂公开 Docker 网络，只通过安全域 fabric 网卡通信；入口节点或发布端口节点会额外挂管理入口，方便选手访问入口服务。
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
            <Text>平台会用 Linux bridge/veth fabric 配置多网卡、固定 IP、显式路由、动态 Flag、启动命令、健康检查和资源限制；镜像不需要内置路由脚本。</Text>
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

const findSavedByTopologyKey = <T extends { id: number; topologyKey?: string }>(
  before: T[],
  after: T[],
  id: number
) => {
  const source = before.find((item) => item.id === id)
  return source?.topologyKey ? after.find((item) => item.topologyKey === source.topologyKey) : undefined
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
      findSavedByTopologyKey(before.networks, after.networks, target.id) ||
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
      findSavedByTopologyKey(before.nodes, after.nodes, target.id) ||
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
    findSavedByTopologyKey(before.edges, after.edges, target.id) ||
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
  const modals = useModals()
  const { screenToFlowPosition } = useReactFlow()
  const [config, setConfig] = useState<PenetrationConfigModel>()
  const [templates, setTemplates] = useState<ImageTemplateLite[]>([])
  const templatesRef = useRef<ImageTemplateLite[]>([])
  const [plan, setPlan] = useState<PenetrationPlanModel>()
  const [environments, setEnvironments] = useState<PenetrationTeamEnvironmentModel[]>([])
  const [access, setAccess] = useState<PenetrationAdminAccessModel[]>([])
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
      const [configRes, templateRes, planRes, envRes, accessRes, submissionRes, eventRes] = await Promise.all([
        penetrationAdminApi.getConfig(gameId),
        fetcher('/api/v1/image-templates?page=1&pageSize=100'),
        penetrationAdminApi.plan(gameId),
        penetrationAdminApi.getEnvironments(gameId),
        penetrationAdminApi.getAccess(gameId),
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
      setAccess(accessRes.data)
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
        showNotification({ color: 'teal', message: '拓扑版本已发布', icon: <Icon path={mdiPublish} size={1} /> })
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
    const edge = makeEdge(newId(config.edges), source, target, '访问策略')
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
        ? config.networks.find((network) => network.id === target.id)?.name ?? '安全域'
        : target.kind === 'node'
          ? config.nodes.find((node) => node.id === target.id)?.name ?? '节点'
          : config.edges.find((edge) => edge.id === target.id)?.label ?? '访问策略'
    modals.openConfirmModal({
      title: '确认删除编排对象',
      children: (
        <Text size="sm" className="yy-readable-text">
          将删除“{selectedName}”。删除安全域会同时删除其中节点和相关访问策略，此操作需要保存后才会生效。
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

  const restartRuntimeNode = (item: PenetrationAdminAccessModel) => {
    modals.openConfirmModal({
      title: '确认重建整队环境',
      children: (
        <Stack gap={6}>
          <Text size="sm">将先清理队伍“{item.teamName}”的当前环境，再按该队已部署版本重建整队渗透环境。</Text>
          <Text size="xs" className="yy-readable-text">触发来源：{item.nodeName}。重建期间该队入口会短暂不可用。</Text>
        </Stack>
      ),
      labels: { confirm: '确认重建', cancel: '取消' },
      confirmProps: { color: 'yellow' },
      onConfirm: () => void executeRestartRuntimeNode(item.runtimeNodeId),
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
  const runtimeRouteRows = environments.flatMap((env) => (env.runtimeRoutes ?? []).map((route) => ({ env, route })))
  const runtimeNodeRows = environments.flatMap((env) => env.runtimeNodes.map((node) => ({ env, node })))

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
          <Tooltip label="取消当前正在执行的部署任务，已完成队伍保持运行">
            <Button color="yellow" variant="light" leftSection={<Icon path={mdiStop} size={0.85} />} onClick={() => runAction('cancelDeploy')}>
              取消部署
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
                  校验/计划只检查当前画布，不会保存草稿；发布会先保存并生成可部署版本。连线用于表达允许路径、任务链和可选运行期网络路由；真实隔离由安全域 Docker 网络和多网卡边界执行。
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
                      <Divider label="选手端黑盒信息" />
                      <TextInput
                        label="选手端代号"
                        description="留空时平台会自动显示为入口目标或目标模块编号，不暴露真实资产名称。"
                        value={selectedNode.playerAlias ?? ''}
                        onChange={(event) => updateNode(selectedNode.id, { playerAlias: event.currentTarget.value })}
                      />
                      <Textarea
                        label="选手端说明"
                        description="只填写允许选手看到的任务背景，禁止写入内部 IP、网卡、安全域等管理信息。"
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
                        <Checkbox label="入口节点" checked={selectedNode.isEntry} onChange={(event) => updateNode(selectedNode.id, { isEntry: event.currentTarget.checked, publishPort: event.currentTarget.checked || selectedNode.publishPort })} />
                        <Checkbox label="发布宿主端口" checked={selectedNode.publishPort} onChange={(event) => updateNode(selectedNode.id, { publishPort: event.currentTarget.checked })} />
                        <Checkbox label="允许作为路由节点" checked={selectedNode.allowRouting} onChange={(event) => updateNode(selectedNode.id, { allowRouting: event.currentTarget.checked })} />
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
                        <Title order={4}>访问策略</Title>
                        <ActionIcon color="red" variant="light" onClick={removeSelected}><Icon path={mdiDeleteOutline} size={0.8} /></ActionIcon>
                      </Group>
                      <TextInput label="策略名称" value={selectedEdge.label ?? ''} onChange={(event) => updateEdge(selectedEdge.id, { label: event.currentTarget.value })} />
                      <SimpleGrid cols={2}>
                        <Select label="提示协议" description="不执行协议级过滤" data={protocolOptions} value={selectedEdge.protocol} onChange={(value) => value && updateEdge(selectedEdge.id, { protocol: value as PenetrationProtocol })} />
                        <TextInput label="提示端口范围" description="不执行端口级防火墙" value={selectedEdge.portRange} onChange={(event) => updateEdge(selectedEdge.id, { portRange: event.currentTarget.value })} />
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
                      <SimpleGrid cols={2}>
                        <Select
                          label="执行模式"
                          data={enforcementOptions}
                          value={selectedEdge.enforcementMode ?? PenetrationEnforcementMode.HintOnly}
                          onChange={(value) => value && updateEdge(selectedEdge.id, { enforcementMode: value as PenetrationEnforcementMode })}
                        />
                        <NumberInput label="优先级" min={0} max={10000} value={selectedEdge.priority ?? 100} onChange={(value) => updateEdge(selectedEdge.id, { priority: Number(value || 100) })} />
                      </SimpleGrid>
                      <Checkbox label="进入题目攻击图/迷雾提示" checked={selectedEdge.isRouteHint} onChange={(event) => updateEdge(selectedEdge.id, { isRouteHint: event.currentTarget.checked })} />
                      <YinyuPanel p="xs" className="yy-pentest-preview-box">
                        <Text size="xs" fw={800}>{edgeHintTitle}</Text>
                        <Text size="xs" className="yy-readable-text">{edgeHintText}</Text>
                      </YinyuPanel>
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
                            <Table.Thead><Table.Tr><Table.Th>访问路径</Table.Th><Table.Th>来源</Table.Th><Table.Th>目标</Table.Th><Table.Th>执行结果</Table.Th></Table.Tr></Table.Thead>
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
                                        {policy.isExecutable ? '会写入网络级路由' : '仅提示/审计，不写入路由'}
                                      </Text>
                                      <Text size="xs" className="yy-readable-text">{enforcementLabels[policy.enforcementMode]} · {policy.runtimeSummary}</Text>
                                      {policy.routeNodeName && <Text size="xs" className="yy-readable-text">路由节点：{policy.routeNodeName}，网关：{policy.gatewayIp ?? '自动'}</Text>}
                                      {policy.compileMessage && <Text size="xs" className="yy-readable-text" lineClamp={2}>{policy.compileMessage}</Text>}
                                    </Stack>
                                  </Table.Td>
                                </Table.Tr>
                              )) : (
                                <Table.Tr><Table.Td colSpan={4}>暂无访问路径。至少连接两个资产节点，用于表达任务链和跳板关系。</Table.Td></Table.Tr>
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
                    <Group justify="space-between" align="flex-start">
                      <Stack gap={2}>
                        <Title order={4}>运行观测</Title>
                        <Text size="xs" className="yy-readable-text">
                          这里展示队伍环境、容器/VM 追踪、网络级显式路由和部署时间线。协议/端口字段只作为题目路径提示，不表示端口级防火墙已经生效。
                        </Text>
                      </Stack>
                      <Button size="xs" variant="light" onClick={load}>刷新</Button>
                    </Group>

                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead>
                          <Table.Tr>
                            <Table.Th>队伍环境</Table.Th>
                            <Table.Th>状态</Table.Th>
                            <Table.Th>部署版本</Table.Th>
                            <Table.Th>清理与错误</Table.Th>
                            <Table.Th>操作</Table.Th>
                          </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                          {environments.length ? environments.map((env) => (
                            <Table.Tr key={env.environmentId}>
                              <Table.Td>
                                <Stack gap={2}>
                                  <Text fw={900}>{env.teamName}</Text>
                                  <Text size="xs" className="yy-readable-text">队伍序号 #{env.teamIndex} · {env.networkPrefix || '未分配网段'}</Text>
                                  <Text size="xs" className="yy-readable-text">Worker：{env.workerNodeName ?? '未调度节点'}</Text>
                                </Stack>
                              </Table.Td>
                              <Table.Td>
                                <YinyuStatusPill tone={runtimeStatusTone(env.status)} state={runtimeStatusState(env.status)}>
                                  {runtimeStatusLabels[env.status] ?? env.status}
                                </YinyuStatusPill>
                              </Table.Td>
                              <Table.Td>
                                <Stack gap={2}>
                                  <Text size="sm">v{env.publishedVersion} · {env.runtimeNodeCount} 个资产</Text>
                                  <Text size="xs" className="yy-readable-text">重置 {env.resetCount}/{config.maxResetCount}</Text>
                                  <Text size="xs" className="yy-readable-text">更新：{formatDateTime(env.updatedAt ?? env.createdAt)}</Text>
                                </Stack>
                              </Table.Td>
                              <Table.Td>
                                <Stack gap={2}>
                                  <Text size="sm">{env.cleanupRetryCount > 0 ? `清理已重试 ${env.cleanupRetryCount} 次` : '暂无残留清理任务'}</Text>
                                  {env.nextCleanupAt && <Text size="xs" className="yy-readable-text">下次清理：{formatDateTime(env.nextCleanupAt)}</Text>}
                                  {env.lastError && <Text size="xs" c="red.3" lineClamp={3}>{env.lastError}</Text>}
                                </Stack>
                              </Table.Td>
                              <Table.Td>
                                {needsManualCleanup(env.status) ? (
                                  <Button size="compact-xs" variant="light" color="red" onClick={() => cleanupTeam(env)}>
                                    重新清理残留
                                  </Button>
                                ) : (
                                  <Text size="xs" className="yy-readable-text">无待处理操作</Text>
                                )}
                              </Table.Td>
                            </Table.Tr>
                          )) : (
                            <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无队伍环境。发布并部署后会在这里显示。</Text></Table.Td></Table.Tr>
                          )}
                        </Table.Tbody>
                      </Table>
                    </YinyuTableShell>

                    <YinyuTableShell p="xs">
                      <Group justify="space-between" mb="xs">
                        <Stack gap={0}>
                          <Text fw={900}>节点可溯源列表</Text>
                          <Text size="xs" className="yy-readable-text">当前运行节点在前，历史/失败节点保留容器 ID、入口、镜像和网卡摘要。</Text>
                        </Stack>
                        <Text size="xs" className="yy-readable-text">共 {runtimeNodeRows.length} 个节点记录</Text>
                      </Group>
                      <Table>
                        <Table.Thead>
                          <Table.Tr>
                            <Table.Th>队伍 / 资产</Table.Th>
                            <Table.Th>状态</Table.Th>
                            <Table.Th>容器与镜像</Table.Th>
                            <Table.Th>入口 / 内网</Table.Th>
                            <Table.Th>网卡</Table.Th>
                          </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                          {runtimeNodeRows.length ? runtimeNodeRows
                            .sort((left, right) =>
                              Number(right.node.status === PenetrationRuntimeStatus.Running) -
                              Number(left.node.status === PenetrationRuntimeStatus.Running) ||
                              left.env.teamName.localeCompare(right.env.teamName) ||
                              left.node.nodeName.localeCompare(right.node.nodeName))
                            .map(({ env, node }) => {
                              const accessItem = access.find((item) => item.runtimeNodeId === node.runtimeNodeId)
                              const interfaces = parseRuntimeInterfaces(node.interfaceSummary)
                              const entry = accessItem?.url ?? (node.publicHost && node.publicPort ? `${node.publicHost}:${node.publicPort}` : node.publicPort ? `端口 ${node.publicPort}` : '')
                              return (
                                <Table.Tr key={`${env.environmentId}-${node.runtimeNodeId}`}>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      <Text fw={800}>{env.teamName} / {node.nodeName}</Text>
                                      <Text size="xs" className="yy-readable-text">拓扑键：{shortText(node.topologyNodeKey, 18)}</Text>
                                      <Text size="xs" className="yy-readable-text">创建：{formatDateTime(node.createdAt)}</Text>
                                    </Stack>
                                  </Table.Td>
                                  <Table.Td>
                                    <YinyuStatusPill tone={runtimeStatusTone(node.status)} state={runtimeStatusState(node.status)}>
                                      {runtimeStatusLabels[node.status] ?? node.status}
                                    </YinyuStatusPill>
                                  </Table.Td>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      <Text size="xs" className="yy-readable-text">容器：{shortText(node.containerId)}</Text>
                                      <Text size="xs" className="yy-readable-text">状态：{node.containerStatus ?? '-'}</Text>
                                      <Text size="xs" className="yy-readable-text" lineClamp={1}>镜像：{node.image ?? '-'}</Text>
                                    </Stack>
                                  </Table.Td>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      <Text size="xs" className="yy-readable-text">入口：{entry || '未公开'}</Text>
                                      <Text size="xs" className="yy-readable-text">内网：{node.ipAddress || accessItem?.internalIp || '-'}</Text>
                                      {accessItem && (
                                        <Button size="compact-xs" variant="light" onClick={() => restartRuntimeNode(accessItem)}>
                                          重建整队环境
                                        </Button>
                                      )}
                                    </Stack>
                                  </Table.Td>
                                  <Table.Td>
                                    <Stack gap={2}>
                                      {interfaces.length ? interfaces.slice(0, 3).map((iface, index) => (
                                        <Text size="xs" className="yy-readable-text" lineClamp={1} key={`${node.runtimeNodeId}-${index}`}>
                                          {iface.interfaceName ?? `eth${index}`} · {iface.networkName ?? node.networkName} · {iface.ipAddress ?? '-'} / {iface.cidr ?? '-'}{iface.isPrimary ? ' · 主网卡' : ''}
                                        </Text>
                                      )) : (
                                        <Text size="xs" className="yy-readable-text">{node.networkName || '-'} · {node.ipAddress || '-'}</Text>
                                      )}
                                      {interfaces.length > 3 && <Text size="xs" className="yy-readable-text">另有 {interfaces.length - 3} 块网卡</Text>}
                                    </Stack>
                                  </Table.Td>
                                </Table.Tr>
                              )
                            }) : (
                            <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无运行节点。</Text></Table.Td></Table.Tr>
                          )}
                        </Table.Tbody>
                      </Table>
                    </YinyuTableShell>

                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead><Table.Tr><Table.Th>队伍</Table.Th><Table.Th>网络级路由</Table.Th><Table.Th>路径</Table.Th><Table.Th>执行摘要</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>
                          {runtimeRouteRows.length ? runtimeRouteRows.map(({ env, route }) => (
                            <Table.Tr key={`${env.environmentId}-${route.id}`}>
                              <Table.Td>{env.teamName}</Table.Td>
                              <Table.Td>
                                <Stack gap={2}>
                                  <Text size="sm" fw={800}>{route.label}</Text>
                                  <YinyuStatusPill tone={routeStatusTone(route.status)} state={route.status === PenetrationRouteStatus.RouteApplied ? 'running' : 'idle'}>
                                    {routeStatusLabels[route.status]}
                                  </YinyuStatusPill>
                                  <Text size="xs" fw={800} c={route.isExecutable ? 'teal.2' : 'dimmed'}>
                                    {route.isExecutable ? '网络级路由记录' : '提示/审计记录'}
                                  </Text>
                                  <Text size="xs" className="yy-readable-text">{enforcementLabels[route.enforcementMode]}</Text>
                                </Stack>
                              </Table.Td>
                              <Table.Td>
                                <Text size="xs" className="yy-readable-text">
                                  {route.sourceNetworkName ?? '-'} ({route.sourceCidr ?? '-'}) → {route.targetNetworkName ?? '-'} ({route.targetCidr ?? '-'})
                                </Text>
                                {route.routeNodeName && <Text size="xs" className="yy-readable-text">经由：{route.routeNodeName} · 网关：{route.gatewayIp ?? '-'}</Text>}
                                {route.appliedAt && <Text size="xs" className="yy-readable-text">应用：{formatDateTime(route.appliedAt)}</Text>}
                              </Table.Td>
                              <Table.Td>
                                <Stack gap={2}>
                                  <Text size="xs" className="yy-readable-text" lineClamp={2}>{route.message ?? '无执行说明'}</Text>
                                  {route.commandSummary && <Text size="xs" className="yy-readable-text" lineClamp={2}>{route.commandSummary}</Text>}
                                </Stack>
                              </Table.Td>
                            </Table.Tr>
                          )) : (
                            <Table.Tr><Table.Td colSpan={4}><Text size="sm" className="yy-readable-text">暂无运行期路由记录。HintOnly 策略只会影响选手攻击图，不会生成网络级路由。</Text></Table.Td></Table.Tr>
                          )}
                        </Table.Tbody>
                      </Table>
                    </YinyuTableShell>

                    <YinyuTableShell p="xs">
                      <Table>
                        <Table.Thead><Table.Tr><Table.Th>提交队伍</Table.Th><Table.Th>得分项</Table.Th><Table.Th>状态</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>{submissions.slice(0, 12).map((item) => <Table.Tr key={item.id}><Table.Td>{item.teamName}</Table.Td><Table.Td>{item.nodeName} / {item.itemTitle}</Table.Td><Table.Td>{item.status}</Table.Td></Table.Tr>)}</Table.Tbody>
                      </Table>
                    </YinyuTableShell>

                    <YinyuTableShell p="xs">
                      <Group justify="space-between" mb="xs">
                        <Stack gap={0}>
                          <Text fw={800}>部署时间线</Text>
                          <Text size="xs" className="yy-readable-text">
                            共 {deploymentEventTotal} 条事件，第 {deploymentEventPage} 页
                          </Text>
                        </Stack>
                        <Group gap="xs">
                          <Button size="compact-xs" variant="light" disabled={deploymentEventPage <= 1} onClick={() => void loadDeploymentEvents(deploymentEventPage - 1)}>
                            上一页
                          </Button>
                          <Button size="compact-xs" variant="light" disabled={deploymentEventPage * 50 >= deploymentEventTotal} onClick={() => void loadDeploymentEvents(deploymentEventPage + 1)}>
                            下一页
                          </Button>
                        </Group>
                      </Group>
                      <Table>
                        <Table.Thead>
                          <Table.Tr>
                            <Table.Th>队伍</Table.Th>
                            <Table.Th>级别</Table.Th>
                            <Table.Th>阶段</Table.Th>
                            <Table.Th>内容</Table.Th>
                          </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                          {deploymentEvents.length > 0 ? (
                            deploymentEvents.map((event) => (
                              <Table.Tr key={`${event.environmentId}-${event.id}`}>
                                <Table.Td>
                                  <Stack gap={2}>
                                    <Text size="sm">{event.teamName}</Text>
                                    <Text size="xs" className="yy-readable-text">{formatDateTime(event.createdAt)}</Text>
                                  </Stack>
                                </Table.Td>
                                <Table.Td>
                                  <YinyuStatusPill tone={deploymentEventTone(event.level)} state={event.level === PenetrationDeploymentEventLevel.Error ? 'alert' : 'busy'}>
                                    {deploymentEventLabel[event.level]}
                                  </YinyuStatusPill>
                                </Table.Td>
                                <Table.Td>{event.stage}{event.nodeName ? ` / ${event.nodeName}` : ''}</Table.Td>
                                <Table.Td>
                                  <Stack gap={2}>
                                    <Text size="sm">{event.message}</Text>
                                    {event.detail && <Text size="xs" className="yy-readable-text" lineClamp={2}>{event.detail}</Text>}
                                  </Stack>
                                </Table.Td>
                              </Table.Tr>
                            ))
                          ) : (
                            <Table.Tr><Table.Td colSpan={4}><Text size="sm" className="yy-readable-text">暂无部署事件。</Text></Table.Td></Table.Tr>
                          )}
                        </Table.Tbody>
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
