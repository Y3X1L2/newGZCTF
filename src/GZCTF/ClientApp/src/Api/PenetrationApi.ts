import api, { AnswerResult, ContentType, RequestParams, RequestResponse } from '@Api'

export enum PenetrationDeploymentStatus {
  Draft = 'Draft',
  Published = 'Published',
  Deploying = 'Deploying',
  Running = 'Running',
  Partial = 'Partial',
  Stopped = 'Stopped',
  Failed = 'Failed',
}

export enum PenetrationNodeType {
  Entry = 'Entry',
  Web = 'Web',
  Database = 'Database',
  JumpHost = 'JumpHost',
  Internal = 'Internal',
  DomainControllerReserved = 'DomainControllerReserved',
  Custom = 'Custom',
  Bastion = 'Bastion',
  FirewallRouter = 'FirewallRouter',
  Service = 'Service',
}

export enum PenetrationZoneType {
  Public = 'Public',
  Dmz = 'Dmz',
  Business = 'Business',
  Data = 'Data',
  Operations = 'Operations',
  Management = 'Management',
  Custom = 'Custom',
}

export enum PenetrationDefaultPolicy {
  DenyAll = 'DenyAll',
  AllowInternal = 'AllowInternal',
}

export enum PenetrationPolicyScope {
  Node = 'Node',
  Network = 'Network',
}

export enum PenetrationPolicyAction {
  Allow = 'Allow',
  Deny = 'Deny',
}

export enum PenetrationEnforcementMode {
  HintOnly = 'HintOnly',
  RuntimeRoute = 'RuntimeRoute',
  Both = 'Both',
}

export enum PenetrationRouteStatus {
  HintOnly = 'HintOnly',
  RoutePlanned = 'RoutePlanned',
  RouteApplied = 'RouteApplied',
  RouteFailed = 'RouteFailed',
  Unsupported = 'Unsupported',
}

export enum PenetrationProtocol {
  Tcp = 'Tcp',
  Udp = 'Udp',
  Icmp = 'Icmp',
  Any = 'Any',
}

export enum PenetrationRuntimeStatus {
  Pending = 'Pending',
  Running = 'Running',
  Stopped = 'Stopped',
  Failed = 'Failed',
  CreatingNetworks = 'CreatingNetworks',
  CreatingContainers = 'CreatingContainers',
  CleanupPending = 'CleanupPending',
  Orphaned = 'Orphaned',
  ManualCleanupRequired = 'ManualCleanupRequired',
}

export enum PenetrationDeploymentEventLevel {
  Info = 'Info',
  Success = 'Success',
  Warning = 'Warning',
  Error = 'Error',
}

export interface PenetrationConfigModel {
  gameId: number
  baseCidr: string
  teamSubnetPrefix: number
  networkSubnetPrefix: number
  maxResetCount: number
  publishedVersion: number
  status: PenetrationDeploymentStatus
  networks: PenetrationNetworkModel[]
  nodes: PenetrationNodeModel[]
  interfaces: PenetrationInterfaceModel[]
  edges: PenetrationEdgeModel[]
}

export interface PenetrationNetworkModel {
  id: number
  topologyKey: string
  name: string
  slug: string
  cidr?: string | null
  zoneType: PenetrationZoneType
  trustLevel: number
  description?: string | null
  defaultPolicy: PenetrationDefaultPolicy
  orderIndex: number
  isEntry: boolean
  positionX: number
  positionY: number
  width: number
  height: number
  collapsed: boolean
  previewCidr?: string | null
}

export interface PenetrationNodeModel {
  id: number
  topologyKey: string
  networkId: number
  name: string
  description?: string | null
  playerAlias?: string | null
  playerDescription?: string | null
  nodeType: PenetrationNodeType
  imageTemplateId?: number | null
  imageName?: string | null
  cpuCount: number
  memoryLimit: number
  storageLimit: number
  exposePort: number
  isEntry: boolean
  publishPort: boolean
  allowRouting: boolean
  staticIp?: string | null
  environmentVariables: Record<string, string>
  startCommand?: string | null
  healthCheck?: string | null
  reservedAdRole?: string | null
  positionX: number
  positionY: number
  orderIndex: number
  previewIp?: string | null
  interfaces: PenetrationInterfaceModel[]
  scoreItems: PenetrationScoreItemModel[]
}

export interface PenetrationInterfaceModel {
  id: number
  topologyKey: string
  nodeId: number
  networkId: number
  name: string
  staticIp?: string | null
  previewIp?: string | null
  isPrimary: boolean
  isManagement: boolean
  orderIndex: number
}

export interface PenetrationEdgeModel {
  id: number
  topologyKey: string
  sourceNodeId: number
  targetNodeId: number
  sourceKind: PenetrationPolicyScope
  sourceId: number
  targetKind: PenetrationPolicyScope
  targetId: number
  protocol: PenetrationProtocol
  portRange: string
  policyAction: PenetrationPolicyAction
  isRouteHint: boolean
  enforcementMode: PenetrationEnforcementMode
  priority: number
  label?: string | null
  description?: string | null
}

export interface PenetrationScoreItemModel {
  id: number
  topologyKey: string
  title: string
  description?: string | null
  category: string
  score: number
  isDynamic: boolean
  staticFlag?: string | null
  flagTemplate?: string | null
  maxAttempts: number
  isVisible: boolean
  isCheckpoint: boolean
  prerequisiteItemIds: number[]
  orderIndex: number
}

export interface PenetrationValidationModel {
  valid: boolean
  errors: string[]
  warnings: string[]
}

export interface PenetrationPlanModel {
  gameId: number
  teamCount: number
  sampleTeamPrefix: string
  validation: PenetrationValidationModel
  networks: PenetrationPlanNetworkModel[]
  nodes: PenetrationPlanNodeModel[]
  policies: PenetrationPlanPolicyModel[]
  flags: PenetrationPlanFlagModel[]
  deploymentSteps: string[]
}

export interface PenetrationPlanNetworkModel {
  networkId: number
  networkName: string
  slug: string
  zoneType: PenetrationZoneType
  cidr: string
  defaultPolicy: PenetrationDefaultPolicy
  isInternal: boolean
}

export interface PenetrationPlanNodeModel {
  nodeId: number
  nodeName: string
  nodeType: PenetrationNodeType
  image: string
  publishPort: boolean
  exposePort: number
  interfaces: PenetrationPlanInterfaceModel[]
  adminAccessHint?: string | null
}

export interface PenetrationPlanInterfaceModel {
  interfaceId: number
  name: string
  networkId: number
  networkName: string
  networkSlug: string
  cidr: string
  ipAddress: string
  isPrimary: boolean
  isManagement: boolean
  isInternal: boolean
}

export interface PenetrationPlanPolicyModel {
  policyId: number
  label: string
  source: string
  target: string
  protocol: PenetrationProtocol
  portRange: string
  action: PenetrationPolicyAction
  isRouteHint: boolean
  enforcementMode: PenetrationEnforcementMode
  routeStatus: PenetrationRouteStatus
  runtimeSummary: string
  routeNodeName?: string | null
  sourceNetworkName?: string | null
  targetNetworkName?: string | null
  gatewayIp?: string | null
  compileMessage?: string | null
  isExecutable: boolean
}

export interface PenetrationPlanFlagModel {
  scoreItemId: number
  nodeId: number
  nodeName: string
  title: string
  category: string
  score: number
  isDynamic: boolean
  preview: string
}

export interface PenetrationWorkspaceModel {
  gameId: number
  teamId: number
  teamName: string
  status: PenetrationRuntimeStatus
  resetCount: number
  maxResetCount: number
  nodes: PenetrationWorkspaceNodeModel[]
}

export interface PenetrationTeamEnvironmentModel {
  environmentId: number
  teamId: number
  teamName: string
  workerNodeId?: string | null
  workerNodeName?: string | null
  networkPrefix: string
  teamIndex: number
  publishedVersion: number
  status: PenetrationRuntimeStatus
  resetCount: number
  runtimeNodeCount: number
  createdAt: number
  updatedAt?: number | null
  lastError?: string | null
  cleanupRetryCount: number
  nextCleanupAt?: number | null
  lastCleanupAttemptAt?: number | null
  events: PenetrationDeploymentEventModel[]
  runtimeNodes: PenetrationRuntimeNodeModel[]
  runtimeRoutes: PenetrationRuntimeRouteModel[]
}

export interface PenetrationRuntimeNodeModel {
  runtimeNodeId: number
  topologyNodeId: number
  topologyNodeKey: string
  nodeName: string
  networkName: string
  ipAddress: string
  adminAccessUrl?: string | null
  publicPort?: number | null
  status: PenetrationRuntimeStatus
  createdAt: number
  containerGuid?: string | null
  containerId?: string | null
  containerStatus?: string | null
  image?: string | null
  publicHost?: string | null
  interfaceSummary: string
}

export interface PenetrationRuntimeRouteModel {
  id: number
  edgeTopologyKey: string
  label: string
  enforcementMode: PenetrationEnforcementMode
  status: PenetrationRouteStatus
  routeNodeKey?: string | null
  routeNodeName?: string | null
  sourceNetworkName?: string | null
  targetNetworkName?: string | null
  sourceCidr?: string | null
  targetCidr?: string | null
  gatewayIp?: string | null
  commandSummary?: string | null
  message?: string | null
  isExecutable: boolean
  createdAt: number
  appliedAt?: number | null
}

export interface PenetrationDeploymentEventModel {
  id: number
  environmentId: number
  teamId: number
  teamName: string
  stage: string
  level: PenetrationDeploymentEventLevel
  message: string
  nodeName?: string | null
  detail?: string | null
  userId?: string | null
  createdAt: number
}

export interface PenetrationAdminAccessModel {
  runtimeNodeId: number
  teamId: number
  teamName: string
  nodeId: number
  nodeName: string
  status: PenetrationRuntimeStatus
  workerNodeName: string
  containerId: string
  internalIp: string
  interfaceSummary: string
  host?: string | null
  publicPort?: number | null
  url?: string | null
  exposePort: number
}

export interface PenetrationWorkspaceNodeModel {
  id: number
  networkId: number
  topologyKey: string
  name: string
  description?: string | null
  nodeType: PenetrationNodeType
  runtimeStatus: PenetrationRuntimeStatus
  scoreItems: PenetrationWorkspaceScoreItemModel[]
}

export interface PenetrationWorkspaceScoreItemModel {
  id: number
  topologyKey: string
  title: string
  description?: string | null
  category: string
  score: number
  solved: boolean
  attempts: number
  maxAttempts: number
  isCheckpoint: boolean
  prerequisiteItemIds: number[]
  prerequisiteItemKeys: string[]
}

export interface PenetrationSubmitResultModel {
  accepted: boolean
  score: number
  message: string
}

export interface PenetrationWorkspaceUpdateModel {
  gameId: number
  teamId: number
  publishedVersion: number
  time: number
}

export interface PenetrationScoreboardItemModel {
  rank: number
  teamId: number
  teamName: string
  score: number
  solvedCount: number
  lastSubmissionTime: number
}

export interface PenetrationSubmissionLogModel {
  id: number
  time: number
  teamId: number
  teamName: string
  userName: string
  nodeName: string
  itemTitle: string
  category: string
  score: number
  status: AnswerResult
}

export interface TeamLabVpnConfigModel {
  gameId: number
  teamId: number
  teamName: string
  endpoint: string
  clientAddress: string
  allowedIPs: string
  dns: string
  configVersion: number
  fileName: string
  configText: string
}

export interface PenetrationArrayResponse<T> {
  data: T[]
  length: number
  total: number
}

export interface ImageTemplateLite {
  id: number
  name: string
  registryUrl?: string | null
  osType?: string | number
  imageType?: string | number
  status?: string | number
}

const request = api.request
const json = ContentType.Json

export const penetrationAdminApi = {
  getConfig: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationConfigModel, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  saveConfig: (gameId: number, data: PenetrationConfigModel, params: RequestParams = {}) =>
    request<PenetrationConfigModel, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}`,
      method: 'PUT',
      body: data,
      type: json,
      format: 'json',
      ...params,
    }),
  validate: (gameId: number, data?: PenetrationConfigModel, params: RequestParams = {}) =>
    request<PenetrationValidationModel, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/validate`,
      method: 'POST',
      body: data,
      type: data ? json : undefined,
      format: 'json',
      ...params,
    }),
  plan: (gameId: number, data?: PenetrationConfigModel, params: RequestParams = {}) =>
    request<PenetrationPlanModel, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/plan`,
      method: 'POST',
      body: data,
      type: data ? json : undefined,
      format: 'json',
      ...params,
    }),
  publish: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationConfigModel, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/publish`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  deploy: (gameId: number, force = false, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/deploy`,
      method: 'POST',
      query: { force },
      format: 'json',
      ...params,
    }),
  cancelDeploy: (gameId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/deploy/cancel`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  stop: (gameId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/stop`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  rebuildTeam: (gameId: number, teamId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/rebuild`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  cleanupTeam: (gameId: number, teamId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/cleanup`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getAccess: (gameId: number, teamId?: number, params: RequestParams = {}) =>
    request<PenetrationAdminAccessModel[], RequestResponse>({
      path: teamId
        ? `/api/admin/pentest/games/${gameId}/teams/${teamId}/access`
        : `/api/admin/pentest/games/${gameId}/access`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  rebuildTeamByRuntimeNode: (runtimeNodeId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/runtime-nodes/${runtimeNodeId}/rebuild-team`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  restartRuntimeNode: (runtimeNodeId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/admin/pentest/runtime-nodes/${runtimeNodeId}/restart`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationScoreboardItemModel[], RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/scoreboard`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getEnvironments: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationTeamEnvironmentModel[], RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/environments`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getDeploymentEvents: (gameId: number, count = 50, skip = 0, environmentId?: number, params: RequestParams = {}) =>
    request<PenetrationArrayResponse<PenetrationDeploymentEventModel>, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/deployment-events`,
      method: 'GET',
      query: { count, skip, environmentId },
      format: 'json',
      ...params,
    }),
  getSubmissions: (gameId: number, count = 50, skip = 0, params: RequestParams = {}) =>
    request<PenetrationArrayResponse<PenetrationSubmissionLogModel>, RequestResponse>({
      path: `/api/admin/pentest/games/${gameId}/submissions`,
      method: 'GET',
      query: { count, skip },
      format: 'json',
      ...params,
    }),
}

export const penetrationPlayerApi = {
  getWorkspace: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationWorkspaceModel, RequestResponse>({
      path: `/api/pentest/games/${gameId}/workspace`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getTeamLabVpnConfig: (gameId: number, params: RequestParams = {}) =>
    request<TeamLabVpnConfigModel, RequestResponse>({
      path: `/api/pentest/games/${gameId}/teamlab/vpn-config`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  submit: (gameId: number, scoreItemId: number, flag: string, params: RequestParams = {}) =>
    request<PenetrationSubmitResultModel, RequestResponse>({
      path: `/api/pentest/games/${gameId}/submit`,
      method: 'POST',
      body: { scoreItemId, flag },
      type: json,
      format: 'json',
      ...params,
    }),
  reset: (gameId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({
      path: `/api/pentest/games/${gameId}/reset`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationScoreboardItemModel[], RequestResponse>({
      path: `/api/pentest/games/${gameId}/scoreboard`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
}
