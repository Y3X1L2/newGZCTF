import { Badge, Group, ScrollArea, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import {
  Handle,
  NodeResizer,
  Position,
  type Edge,
  type Node,
  type NodeProps,
  type NodeTypes,
} from '@xyflow/react'
import { FC, memo } from 'react'
import { YinyuDrawerBody, YinyuPanel } from '@Components/yinyu/YinyuUI'
import {
  ImageTemplateLite,
  PenetrationConfigModel,
  PenetrationDefaultPolicy,
  PenetrationDeploymentStatus,
  PenetrationEdgeModel,
  PenetrationEnforcementMode,
  PenetrationInterfaceModel,
  PenetrationNetworkModel,
  PenetrationNodeModel,
  PenetrationNodeType,
  PenetrationPolicyAction,
  PenetrationPolicyScope,
  PenetrationProtocol,
  PenetrationRouteStatus,
  PenetrationRuntimeStatus,
  PenetrationScoreItemModel,
  PenetrationZoneType,
} from '@Api/PenetrationApi'

export const NETWORK_W = 620
export const NETWORK_H = 420
export const NODE_W = 238
export const NODE_H = 148

export type SelectedTarget =
  | { kind: 'network'; id: number }
  | { kind: 'node'; id: number }
  | { kind: 'edge'; id: number }
  | undefined

export type SegmentData = Record<string, unknown> & {
  label: string
  slug: string
  cidr: string
  zoneType: PenetrationZoneType
  nodeCount: number
}

export type AssetData = Record<string, unknown> & {
  label: string
  nodeType: PenetrationNodeType
  templateName: string
  interfaceLabel: string
  scoreLabel: string
}

export const teamLabZoneTypes = [
  PenetrationZoneType.Dmz,
  PenetrationZoneType.Business,
  PenetrationZoneType.Data,
  PenetrationZoneType.Operations,
  PenetrationZoneType.Management,
  PenetrationZoneType.Custom,
]

export const teamLabNodeTypes = [
  PenetrationNodeType.Web,
  PenetrationNodeType.Database,
  PenetrationNodeType.JumpHost,
  PenetrationNodeType.Internal,
  PenetrationNodeType.DomainControllerReserved,
  PenetrationNodeType.Custom,
  PenetrationNodeType.Bastion,
  PenetrationNodeType.FirewallRouter,
  PenetrationNodeType.Service,
]

export const zoneLabels: Partial<Record<PenetrationZoneType, string>> = {
  [PenetrationZoneType.Dmz]: '业务接入区',
  [PenetrationZoneType.Business]: '业务区',
  [PenetrationZoneType.Data]: '数据区',
  [PenetrationZoneType.Operations]: '运维区',
  [PenetrationZoneType.Management]: '管理区',
  [PenetrationZoneType.Custom]: '自定义',
}

export const nodeTypeLabels: Partial<Record<PenetrationNodeType, string>> = {
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

export const runtimeStatusLabels: Record<PenetrationRuntimeStatus, string> = {
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

export const needsManualCleanup = (status: PenetrationRuntimeStatus) =>
  status === PenetrationRuntimeStatus.CleanupPending ||
  status === PenetrationRuntimeStatus.Orphaned ||
  status === PenetrationRuntimeStatus.ManualCleanupRequired

export const deployableEnforcementModes: Array<PenetrationEnforcementMode.RuntimeRoute | PenetrationEnforcementMode.Both> = [
  PenetrationEnforcementMode.Both,
  PenetrationEnforcementMode.RuntimeRoute,
]

export const enforcementLabels: Record<PenetrationEnforcementMode.RuntimeRoute | PenetrationEnforcementMode.Both, string> = {
  [PenetrationEnforcementMode.RuntimeRoute]: '运行期网络路由',
  [PenetrationEnforcementMode.Both]: '提示 + 运行期路由',
}

export const edgeHintTitle = '路由关系用于 TeamLab 内网连通'
export const edgeHintText = '当前版本通过队伍 VPN 进入 TeamLab 内网，连线表达网段级路由关系；协议和端口只作为出题备注，不作为防火墙规则。'

export const routeStatusLabels: Record<PenetrationRouteStatus, string> = {
  [PenetrationRouteStatus.HintOnly]: '提示路径',
  [PenetrationRouteStatus.RoutePlanned]: '路由可部署',
  [PenetrationRouteStatus.RouteApplied]: '路由已应用',
  [PenetrationRouteStatus.RouteFailed]: '路由失败',
  [PenetrationRouteStatus.Unsupported]: '暂不支持',
}

export const routeStatusTone = (status: PenetrationRouteStatus) => {
  if (status === PenetrationRouteStatus.RouteApplied || status === PenetrationRouteStatus.RoutePlanned) return 'success'
  if (status === PenetrationRouteStatus.RouteFailed || status === PenetrationRouteStatus.Unsupported) return 'danger'
  return 'neutral'
}

export const normalizeZoneType = (value: PenetrationZoneType) =>
  teamLabZoneTypes.includes(value) ? value : PenetrationZoneType.Dmz
export const normalizeNodeType = (value: PenetrationNodeType) =>
  teamLabNodeTypes.includes(value) ? value : PenetrationNodeType.Web
export const zoneLabel = (value: PenetrationZoneType) => zoneLabels[normalizeZoneType(value)] ?? '内网网段'
export const nodeTypeLabel = (value: PenetrationNodeType) => nodeTypeLabels[normalizeNodeType(value)] ?? '资产'
export const zoneOptions = teamLabZoneTypes.map((value) => ({ value, label: zoneLabel(value) }))
export const nodeTypeOptions = teamLabNodeTypes.map((value) => ({ value, label: nodeTypeLabel(value) }))
export const protocolOptions = Object.values(PenetrationProtocol).map((value) => ({ value, label: value.toUpperCase() }))
export const enforcementOptions = deployableEnforcementModes.map((value) => ({ value, label: enforcementLabels[value] }))
export const enforcementLabel = (value: PenetrationEnforcementMode) => {
  if (value === PenetrationEnforcementMode.RuntimeRoute || value === PenetrationEnforcementMode.Both)
    return enforcementLabels[value]

  return '待重新保存为运行期路由'
}

export const flowNetworkId = (id: number) => `network-${id}`
// 临时 ID 递减计数器，确保在 Int32 范围内且全局唯一
let tempIdSeq = 0
export const nextTempId = () => --tempIdSeq
export const newId = (items: { id: number }[]) => Math.min(-1, ...items.map((item) => item.id)) - 1
export const newTopologyKey = (prefix: string, id?: number) =>
  `${prefix}-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}-${Math.abs(id ?? 0)}`
export const enumKey = (value: string | number | undefined | null) => String(value ?? '').toLowerCase()
export const isReadyTeamLabTemplate = (template: ImageTemplateLite) =>
  (enumKey(template.status) === '0' || enumKey(template.status) === 'ready')
export const normalizeConfig = (config: PenetrationConfigModel): PenetrationConfigModel => {
  const networks = config.networks.map((network) => ({
    ...network,
    topologyKey: network.topologyKey || newTopologyKey('network', network.id),
    zoneType: normalizeZoneType(network.zoneType),
    isEntry: false,
  }))
  const nodes = config.nodes.map((node) => ({
    ...node,
    topologyKey: node.topologyKey || newTopologyKey('node', node.id),
    nodeType: normalizeNodeType(node.nodeType),
    isEntry: false,
    publishPort: false,
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
      enforcementMode: edge.enforcementMode ?? PenetrationEnforcementMode.Both,
      priority: edge.priority ?? 100,
    })),
  }
}

export const defaultNetwork = (id: number, orderIndex: number, zoneType = PenetrationZoneType.Custom): PenetrationNetworkModel => ({
  id,
  topologyKey: newTopologyKey('network', id),
  name: `${zoneLabel(zoneType)} ${orderIndex + 1}`,
  slug: zoneType.toLowerCase(),
  cidr: '',
  zoneType,
  trustLevel: zoneType === PenetrationZoneType.Data ? 80 : 50,
  description: '',
  defaultPolicy: PenetrationDefaultPolicy.DenyAll,
  orderIndex,
  isEntry: false,
  positionX: 80 + orderIndex * 700,
  positionY: 90 + (orderIndex % 2) * 60,
  width: NETWORK_W,
  height: NETWORK_H,
  collapsed: false,
})

export const defaultScoreItem = (orderIndex: number): PenetrationScoreItemModel => ({
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

export const defaultNode = (
  id: number,
  network: PenetrationNetworkModel,
  orderIndex: number,
  nodeType = PenetrationNodeType.Internal
): PenetrationNodeModel => {
  return {
    id,
    topologyKey: newTopologyKey('node', id),
    networkId: network.id,
    name: nodeTypeLabel(nodeType),
    description: '',
    playerAlias: '',
    playerDescription: '',
    nodeType,
    imageTemplateId: null,
    imageName: '',
    cpuCount: 10,
    memoryLimit: 512,
    storageLimit: 512,
    exposePort: 80,
    isEntry: false,
    publishPort: false,
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
        isManagement: false,
        orderIndex: 0,
      },
    ],
    scoreItems: [defaultScoreItem(0)],
  }
}

export const fallbackConfig = (gameId: number): PenetrationConfigModel => {
  const network = defaultNetwork(-1, 0, PenetrationZoneType.Dmz)
  network.name = '业务接入网段'
  network.slug = 'service-lan'
  network.description = '选手连接队伍 VPN 后可在该内网网段中发现业务资产。'
  return {
    gameId,
    baseCidr: '10.60.0.0/16',
    teamSubnetPrefix: 24,
    networkSubnetPrefix: 28,
    maxResetCount: 3,
    publishedVersion: 0,
    status: PenetrationDeploymentStatus.Draft,
    networks: [network],
    nodes: [defaultNode(-11, network, 0, PenetrationNodeType.Web)],
    interfaces: [],
    edges: [],
  }
}

export const buildEnterpriseBlueprint = (gameId: number, templates: ImageTemplateLite[], current?: PenetrationConfigModel) => {
  const readyTemplates = templates.filter(isReadyTeamLabTemplate)
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
      key: 'nm-net-service-lan',
      zone: PenetrationZoneType.Dmz,
      name: 'Service / 业务接入网段',
      slug: 'service-lan',
      cidr: '10.10.10.0/24',
      trust: 30,
      x: 60,
      y: 80,
      w: 620,
      h: 430,
      description: '队伍连接 WireGuard 后可扫描发现的业务接入网段，包含企业官网、资源中心和客户上传中心。',
    },
    {
      id: -102,
      key: 'nm-net-biz-core',
      zone: PenetrationZoneType.Business,
      name: 'Business / AI 业务核心区',
      slug: 'biz-core',
      cidr: '192.168.20.0/24',
      trust: 55,
      x: 780,
      y: 110,
      w: 680,
      h: 460,
      description: '文档解析、AI 控制台 API 和任务队列所在的业务内网。',
    },
    {
      id: -103,
      key: 'nm-net-data-plane',
      zone: PenetrationZoneType.Data,
      name: 'Data / 数据与模型区',
      slug: 'data-plane',
      cidr: '172.16.30.0/24',
      trust: 85,
      x: 1580,
      y: 70,
      w: 720,
      h: 520,
      description: '客户数据库、对象存储、密钥服务和模型仓库，承载高分与终局线索。',
    },
    {
      id: -104,
      key: 'nm-net-ops-control',
      zone: PenetrationZoneType.Operations,
      name: 'Operations / 运维控制区',
      slug: 'ops-control',
      cidr: '192.168.40.0/24',
      trust: 70,
      x: 820,
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
    isEntry: false,
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
    routing?: boolean
    interfaces: InterfaceSpec[]
    env?: Record<string, string>
    scores: PenetrationScoreItemModel[]
  }

  const url = (key: string, port?: number) => port ? `{{asset:nm-node-${key}:url:${port}}}` : `{{asset:nm-node-${key}:url}}`
  const host = (key: string) => `{{asset:nm-node-${key}:ip}}`

  const nodeSpecs: NodeSpec[] = [
    {
      id: -202,
      key: 'portal-web',
      serviceKey: 'portal-web',
      name: 'portal-web 企业官网与资源中心',
      alias: '题目 01',
      description: '真实企业门户、产品页、客户案例、资源中心和遗留静态资源。连接队伍 VPN 后由选手在内网中自行扫描发现。',
      playerDescription: '连接队伍 VPN 后，在 NebulaMind 内网中自行扫描和信息收集。',
      type: PenetrationNodeType.Web,
      exposePort: 8080,
      cpu: 5,
      memory: 256,
      storage: 512,
      x: 70,
      y: 96,
      interfaces: [{ net: 'service-lan', primary: true }],
      env: {
        NM_AI_CONSOLE_API_URL: url('ai-console-api'),
        NM_AI_CONSOLE_API_HOST: host('ai-console-api'),
      },
      scores: [
        score(-602, 'PORTAL_HIDDEN_DOCS', 'A 初始侦察', 80, '在 VPN 内网可达的企业门户中发现旧版白皮书/导出页面里的内部跟踪标识。', 0),
        score(-603, 'PORTAL_SOURCEMAP', 'A 初始侦察', 120, '分析生产 Source Map，发现控制台 API、测试租户和调试 Flag。', 1),
      ],
    },
    {
      id: -203,
      key: 'support-upload',
      serviceKey: 'support-upload',
      name: 'support-upload 客户支持上传中心',
      alias: '题目 02',
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
        { net: 'service-lan', primary: true },
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
      alias: '题目 03',
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
      alias: '题目 04',
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
      alias: '题目 05',
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
      alias: '题目 06',
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
        NM_CI_RUNNER_URL: url('ci-runner'),
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
      alias: '题目 07',
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
      alias: '题目 08',
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
      alias: '题目 09',
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
      alias: '题目 10',
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
      alias: '题目 11',
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
      isEntry: false,
      publishPort: false,
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
    enforcementMode: PenetrationEnforcementMode.Both,
    isRouteHint: true,
    priority,
    description,
  })
  const edges: PenetrationEdgeModel[] = [
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
export const makeEdge = (id: number, source: PenetrationNodeModel, target: PenetrationNodeModel, label: string, portRange = 'any'): PenetrationEdgeModel => ({
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
  enforcementMode: PenetrationEnforcementMode.Both,
  priority: 100,
  label,
  description: '',
})

export const SegmentNode = memo(({ data, selected }: NodeProps<Node<SegmentData>>) => (
  <div className={`yy-pentest-segment-frame ${selected ? 'is-selected' : ''}`}>
    <Handle type="target" position={Position.Left} className="yy-pentest-hidden-handle" />
    <Handle type="source" position={Position.Right} className="yy-pentest-hidden-handle" />
    <NodeResizer isVisible={selected} minWidth={380} minHeight={260} />
    <Group justify="space-between" align="flex-start" wrap="nowrap">
      <Stack gap={2}>
        <Group gap={8} wrap="nowrap">
          <Badge size="sm" variant="light" className="yy-pentest-segment-badge">
            {zoneLabel(data.zoneType)}
          </Badge>
          <Text fw={900} className="yy-pentest-segment-title">
            {data.label}
          </Text>
        </Group>
        <Text className="yy-pentest-segment-meta">
          {data.slug} / {data.cidr}
        </Text>
      </Stack>
      <Badge variant="outline">{data.nodeCount} 资产</Badge>
    </Group>
  </div>
))

export const AssetNode = memo(({ data, selected }: NodeProps<Node<AssetData>>) => (
  <div className={`yy-pentest-node-card ${selected ? 'is-selected' : ''}`}>
    <Handle type="target" position={Position.Left} className="yy-pentest-handle" />
    <Handle type="source" position={Position.Right} className="yy-pentest-handle" />
    <Group justify="space-between" gap={8} wrap="nowrap">
      <Text fw={900} className="yy-pentest-node-title">
        {data.label}
      </Text>
      <Badge size="xs" variant="light" className={`yy-pentest-node-type type-${data.nodeType.toLowerCase()}`}>
        {nodeTypeLabel(data.nodeType)}
      </Badge>
    </Group>
    <Text className="yy-pentest-node-template">{data.templateName}</Text>
    <div className="yy-pentest-node-grid">
      <span>{data.interfaceLabel}</span>
      <span>VPN 内网资产</span>
      <span>{data.scoreLabel}</span>
    </div>
  </div>
))

export const nodeTypes: NodeTypes = {
  pentestNetwork: SegmentNode,
  pentestAsset: AssetNode,
}

export const PenetrationUsageGuide: FC = () => (
  <YinyuDrawerBody p="lg" className="yy-pentest-help-doc">
    <ScrollArea.Autosize mah="calc(100dvh - 8rem)">
      <Stack gap="lg">
        <Stack gap={6}>
          <Badge variant="light">TeamLab 内网靶场编排</Badge>
          <Title order={3}>使用说明</Title>
          <Text className="yy-readable-text">
            渗透编排用于构建队伍级 TeamLab 内网、资产网卡、网段路由和分段得分场景。平台负责隔离、IPAM、动态 Flag、运行事实和环境重置；镜像只提供具体服务能力。
          </Text>
        </Stack>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>推荐工作流</Title>
            <Text>1. 先点击“一键生成 TeamLab 内网靶场”，得到业务接入区、业务核心区、数据区和运维区的基础骨架。</Text>
            <Text>2. 从左侧拖入内网网段或资产节点，资产放入某个网段后会自动绑定主网卡。</Text>
            <Text>3. 点击内网网段配置名称、标识、CIDR、默认策略和说明；填写 CIDR 时运行环境原样使用，未填写时由平台自动分配。</Text>
            <Text>4. 点击资产节点选择环境模板、服务端口、资源限制、网卡、环境变量和得分项。</Text>
            <Text>5. 在资产之间连线，表达网段级路由、跳板关系和题目路径，再到“计划”页校验 IPAM 和部署结果。</Text>
            <Text>6. 校验通过后依次保存、发布、部署；部署后可在“运行”页查看队伍环境、VPN 状态、资产清单、提交日志和重建操作。</Text>
          </Stack>
        </YinyuPanel>

        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <YinyuPanel p="md" className="yy-pentest-help-section">
            <Stack gap="xs">
              <Title order={4}>内网网段</Title>
              <Text className="yy-readable-text">
                内网网段代表 TeamLab 里的二层网络和信任边界，例如业务接入区、业务核心区、数据区、运维区。显式 CIDR 会在每支队伍的隔离运行环境中原样使用；留空的网段由平台自动分配。
              </Text>
              <Text className="yy-readable-text">
                所有资产默认只暴露在队伍 VPN 内网中；选手通过 WireGuard 进入本队环境后自行扫描和访问。
              </Text>
            </Stack>
          </YinyuPanel>

          <YinyuPanel p="md" className="yy-pentest-help-section">
            <Stack gap="xs">
              <Title order={4}>资产节点</Title>
              <Text className="yy-readable-text">
                节点代表真实资产角色，例如 Web 服务、数据库、跳板机、堡垒机、防火墙/路由和业务服务。节点角色主要用于场景表达，实际运行内容由环境模板决定。
              </Text>
              <Text className="yy-readable-text">
                TeamLab 资产只接入队伍 VPN 内网；管理端保留运行追踪、网卡事实和重建操作。
              </Text>
            </Stack>
          </YinyuPanel>
        </SimpleGrid>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>网卡、IPAM 与多级内网</Title>
            <Text>每个资产至少需要一张网卡。主网卡决定资产默认所在网段；额外网卡用于实现跳板机、防火墙/路由、堡垒机等跨网段资产。</Text>
            <Text>固定运行 IP 可以人工填写，必须位于对应网段 CIDR 内；留空时平台在运行环境中自动分配。</Text>
            <Text>不要在 Dockerfile 或服务配置里写死队伍 IP。需要知道队伍或 Flag 时，读取平台注入的环境变量。</Text>
          </Stack>
        </YinyuPanel>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>Docker 模板要求</Title>
            <Text>环境模板直接复用平台现有环境模板。TeamLab 支持已就绪的 Docker 与 VM 模板。</Text>
            <Text>服务必须监听模板内配置的服务端口，通常监听 0.0.0.0；选手通过 VPN 内网地址访问。</Text>
            <Text>平台会用 Linux bridge/veth、VM bridge 网卡、固定 IP、网段级路由、动态 Flag、启动命令、健康检查和资源限制完成编排。</Text>
          </Stack>
        </YinyuPanel>

        <YinyuPanel p="md" className="yy-pentest-help-section">
          <Stack gap="xs">
            <Title order={4}>选手侧与计分</Title>
            <Text>选手进入渗透工作台后，只看到队伍 VPN 配置、题目、提交框和剩余重置次数。</Text>
            <Text>每个节点可以配置多个得分项，支持静态 Flag 和动态 Flag。动态 Flag 按比赛、队伍、节点、得分项和发布版本稳定生成，重置环境不会改变答案。</Text>
            <Text>选手重置会销毁并重建本队整套环境，消耗管理员配置的最大重置次数；管理员强制重建不消耗选手次数。</Text>
          </Stack>
        </YinyuPanel>
      </Stack>
    </ScrollArea.Autosize>
  </YinyuDrawerBody>
)

export const getTemplateName = (node: PenetrationNodeModel, templates: ImageTemplateLite[]) =>
  templates.find((template) => template.id === node.imageTemplateId)?.name ??
  (node.imageName?.trim() ? node.imageName : '未绑定环境模板')

export const toFlowNodes = (config: PenetrationConfigModel, templates: ImageTemplateLite[]): Node<SegmentData | AssetData>[] => {
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
      },
      className: `yy-pentest-flow-node type-${node.nodeType.toLowerCase()}`,
      draggable: true,
      selectable: true,
      zIndex: 5,
    }
  })
  return [...networks, ...hosts]
}

export const resolveFlowEdgeEndpoint = (edge: PenetrationEdgeModel, kind: 'source' | 'target', config: PenetrationConfigModel) => {
  const nodeId = kind === 'source' ? edge.sourceNodeId : edge.targetNodeId
  if (nodeId && config.nodes.some((node) => node.id === nodeId)) return String(nodeId)

  const scopeKind = kind === 'source' ? edge.sourceKind : edge.targetKind
  const scopeId = kind === 'source' ? edge.sourceId : edge.targetId
  if (scopeKind === PenetrationPolicyScope.Network && scopeId && config.networks.some((network) => network.id === scopeId)) {
    return flowNetworkId(scopeId)
  }

  return undefined
}

export const toFlowEdges = (config: PenetrationConfigModel): Edge[] =>
  config.edges.flatMap((edge) => {
    const source = resolveFlowEdgeEndpoint(edge, 'source', config)
    const target = resolveFlowEdgeEndpoint(edge, 'target', config)
    if (!source || !target || source === target) return []

    return [{
      id: String(edge.id || `${source}-${target}`),
      source,
      target,
      animated: edge.policyAction === PenetrationPolicyAction.Allow,
      label: edge.label || `${edge.protocol}/${edge.portRange}`,
      className: `yy-pentest-flow-edge action-${edge.policyAction.toLowerCase()}`,
    }]
  })

export const withFlowLayout = (config: PenetrationConfigModel, flowNodes: Node<SegmentData | AssetData>[]): PenetrationConfigModel =>
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

export const findSavedByIndex = <T extends { id: number }>(before: T[], after: T[], id: number) => {
  const index = before.findIndex((item) => item.id === id)
  return index >= 0 ? after[index] : undefined
}

export const findSavedByTopologyKey = <T extends { id: number; topologyKey?: string }>(
  before: T[],
  after: T[],
  id: number
) => {
  const source = before.find((item) => item.id === id)
  return source?.topologyKey ? after.find((item) => item.topologyKey === source.topologyKey) : undefined
}

export const remapSelectedTarget = (
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
