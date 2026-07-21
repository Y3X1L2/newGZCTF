import type {
  ImageStatus,
  ImageType,
  NodeCapability,
  NodeStatus,
  OSType,
  TeamLabFabricStatus,
  TeamLabTunnelStatus,
} from '@Api'

export interface NumberPage<T> {
  total: number
  page: number
  pageSize: number
  items: T[]
}

export interface ImageTemplateSummary {
  id: number
  name: string
  osType: OSType
  imageType: ImageType
  fileSize: number
  status: ImageStatus
  description: string | null
  errorMessage: string | null
  imageHash: string | null
  uploadedAt: number
  registryUrl: string | null
  containsMalware?: boolean
  supportsInstanceCredentials?: boolean
  canManage?: boolean
}

export interface ImageTemplateIdentity {
  id: number
  name: string
  osType: OSType
  imageType: ImageType
  fileSize?: number
  status?: ImageStatus
  registryUrl?: string | null
  errorMessage?: string | null
  imageHash?: string | null
  supportsInstanceCredentials?: boolean
  canManage?: boolean
}

export interface DockerRegistrySummary {
  enabled: boolean
  address: string
  namespace: string
  maxUploadSizeGb: number
}

export interface NodeCapabilityAvailability {
  docker: string | null
  kvm: string | null
  teamLabNetwork: string | null
  teamLabDocker: string | null
  teamLabVm: string | null
}

export interface NodeSummary {
  id: string
  name: string
  hostAddress: string
  status: NodeStatus
  capabilities: NodeCapability
  cpuLoad: number
  memoryLoad: number
  currentContainers: number
  maxContainers: number
  reservedContainers: number
  allocatedContainers: number
  currentVms: number
  maxVms: number
  reservedVms: number
  allocatedVms: number
  usedPorts: number
  totalPorts: number
  portPoolStart: number
  portPoolEnd: number
  portPoolMode: string
  lastHeartbeat: number | null
  isSchedulable: boolean
  isLocal: boolean
  agentPort: number
  teamLabNetworkEnabled: boolean
  teamLabTunnelStatus: TeamLabTunnelStatus
  teamLabTunnelIp: string | null
  teamLabTunnelLastHandshake: number | null
  teamLabTunnelLastError: string | null
  teamLabTunnelConfigVersion: number
  teamLabAgentVersion: string | null
  teamLabProtocolVersion: number
  teamLabFabricIp: string | null
  teamLabFabricStatus: TeamLabFabricStatus
  teamLabCapabilitiesJson: string | null
  canHostTeamLab: boolean
  canHostTeamLabFabric: boolean
  canHostTeamLabDocker: boolean
  canHostTeamLabVm: boolean
  unschedulableReasons: string[]
  unschedulableByCapability: NodeCapabilityAvailability
  schedulableCapabilities: string[]
}

export interface NodeResourceItem {
  kind: string
  id: string
  name: string
  status: string
  isActive: boolean
  startedAt: number
  expectedStopAt: number | null
  stoppedAt: number | null
  duration: string
  image: string | null
  runtimeId: string | null
  entry: string | null
  ip: string | null
  port: number | null
  gameId: number | null
  gameTitle: string | null
  challengeId: number | null
  challengeTitle: string | null
  challengeCategory: string | null
  teamId: number | null
  teamName: string | null
  userId: string | null
  userName: string | null
  providerName: string | null
  osType: string | null
}

export interface NodeResourcePage extends NumberPage<NodeResourceItem> {
  nodeId: string
  nodeName: string
  runningCount: number
  containerCount: number
  vmCount: number
  pentestCount: number
  teamLabCount: number
}

export interface NodeDeployResult {
  success: boolean
  nodeId: string
  nodeName: string | null
  capabilities: NodeCapability
  message: string
}

export interface NodeUpdateResult {
  id: string
  isSchedulable: boolean
  isLocal: boolean
  maxContainers: number
  maxVms: number
}

export interface DeploymentTask {
  id: string
  correlationId?: string
  ticketId?: string | null
  targetId?: string | null
  kind?: number | null
  type?: number | null
  action?: number
  operation?: number
  stage?: number
  actionLabel: string
  typeLabel: string
  requestLabel: string
  ownerLabel: string | null
  gameLabel: string | null
  challengeLabel: string | null
  image: string | null
  targetNodeId: string | null
  targetNodeName: string | null
  targetNodeHost: string | null
  targetNodeLabel: string
  statusLabel: string
  statusKey: string
  status: number
  dockerSlots: number
  vmSlots: number
  queuePosition: number
  peopleAhead: number
  result?: string | null
  stageMessage?: string | null
  blockedReasonCode?: string | null
  errorMessage: string | null
  createdAt: number
  startedAt: number | null
  completedAt: number | null
}

export type DeploymentQueueContract = 'deployment-queue' | 'deployment-targets'

export interface DeploymentTaskPage {
  contract: DeploymentQueueContract
  items: DeploymentTask[]
  total: number | null
  page: number | null
  pageSize: number
  nextCursor: string | null
}

export interface DeploymentTaskDetail {
  id: string
  correlationId: string
  targetNodeId: string | null
  kind: number | null
  operation: number | null
  status: number | null
  stage: number | null
  targetNodeName: string | null
  targetNodeHost: string | null
  resultHost: string | null
  resultPort: number | null
  subjectDisplayName: string | null
  resourceDisplayName: string | null
  createdAt: number
  startedAt: number | null
  completedAt: number | null
  errorMessage: string | null
  errorCategory: string | number | null
  errorCode: string | null
  retryable: boolean | null
}

export interface AdminLogEntry {
  id?: number
  time: number
  name: string | null
  level: string | null
  ip: string | null
  msg: string | null
  status: string | null
  correlationId?: string | null
  traceId?: string | null
  eventCode?: string | null
  errorCategory?: string | null
  errorCode?: string | null
  workerNodeId?: string | null
  workerNodeName?: string | null
  deploymentTicketId?: string | null
  resourceType?: string | null
  resourceId?: string | null
  resourceDisplayName?: string | null
}

export interface AdminLogPage {
  contract: 'cursor' | 'offset'
  items: AdminLogEntry[]
  nextCursor: string | null
  offset: number
}

export interface LegacyContainerInstance {
  team?: { id?: number; name?: string; avatar?: string | null } | null
  challenge?: { id?: number; title?: string; category?: string | null } | null
  image?: string
  containerGuid?: string
  containerId?: string
  startedAt?: number
  expectStopAt?: number
  ip?: string
  port?: number
}

export interface LegacyContainerInstancePage {
  data: LegacyContainerInstance[]
  length: number
  total: number
}

export interface GlobalInstanceItem extends NodeResourceItem {
  nodeId: string
  nodeName: string
}

export interface GlobalInstanceNodeFailure {
  nodeId: string
  nodeName: string
  message: string
}

export interface GlobalInstanceInventory {
  source: 'node-resources' | 'legacy-containers'
  items: GlobalInstanceItem[]
  totalNodes: number
  loadedNodes: number
  failures: GlobalInstanceNodeFailure[]
  collectedAt: number
}
