export type TeamLabAssetKind = 'docker' | 'vm'
export type TeamLabInfrastructureKind = 'managed-switch' | 'managed-router'
export type TeamLabConnectionDirection = 'from-to' | 'bidirectional'
export type TeamLabDependencyCondition = 'network-ready' | 'guest-ready' | 'service-ready'
export type TeamLabEndpointObservationMode = 'disabled' | 'optional' | 'required'
export type TeamLabHealthCheckKind = 'tcp' | 'http'
export type TeamLabImageType = 'docker' | 'qcow2' | 'ova' | 'vmdk'
export type TeamLabRuntimeStatus =
  | 'pending'
  | 'planning'
  | 'scheduled'
  | 'deploying'
  | 'probing'
  | 'running'
  | 'failed'
  | 'cleanup-pending'
  | 'paused'
  | 'destroying'
  | 'destroyed'

export interface TeamLabAddressPool {
  poolCidr: string
  runtimePrefixLength: number
}

export interface TeamLabTopologyNetwork {
  key: string
  name: string
  addressPool: TeamLabAddressPool
  isEntry: boolean
  orderIndex: number
}

export interface TeamLabTopologyInterface {
  key: string
  networkKey: string
  hostOffset: number
  primary: boolean
  orderIndex: number
}

export interface TeamLabAssetResources {
  cpuUnits: number
  memoryMiB: number
  storageMiB: number
}

export interface TeamLabHealthCheck {
  kind: TeamLabHealthCheckKind
  port: number
}

export interface TeamLabTopologyAsset {
  key: string
  name: string
  kind: TeamLabAssetKind
  imageTemplateId: number
  resources: TeamLabAssetResources
  interfaces: readonly TeamLabTopologyInterface[]
  exposePort: number | null
  healthCheck: TeamLabHealthCheck | null
  orderIndex: number
  endpointObservation: TeamLabEndpointObservationMode
  devicePackageId?: number | null
  deviceParameters?: unknown
  connectorId?: string | null
}

export interface TeamLabTopologyInfrastructure {
  key: string
  name: string
  kind: TeamLabInfrastructureKind
  interfaces: readonly TeamLabTopologyInterface[]
  networkKey: string | null
}

export interface TeamLabTopologyConnection {
  key: string
  fromNetworkKey: string
  toNetworkKey: string
  viaAssetKey: string | null
  viaNodeKey: string | null
  direction: TeamLabConnectionDirection
}

export interface TeamLabTopologyDependency {
  assetKey: string
  dependsOnKey: string
  condition: TeamLabDependencyCondition
}

export interface TeamLabObservationPolicy {
  flowMetadataEnabled: boolean
  onDemandPcapEnabled: boolean
  endpointObservation: TeamLabEndpointObservationMode
}

export interface TeamLabTopologyDefinition {
  name: string
  networks: readonly TeamLabTopologyNetwork[]
  infrastructure: readonly TeamLabTopologyInfrastructure[]
  assets: readonly TeamLabTopologyAsset[]
  connections: readonly TeamLabTopologyConnection[]
  dependencies: readonly TeamLabTopologyDependency[]
  observation: TeamLabObservationPolicy
}

export interface TeamLabEditorItem {
  x: number
  y: number
  width: number | null
  height: number | null
  collapsed: boolean
}

export interface TeamLabTopologyEditor {
  networks: Readonly<Record<string, TeamLabEditorItem>>
  assets: Readonly<Record<string, TeamLabEditorItem>>
  infrastructure: Readonly<Record<string, TeamLabEditorItem>>
}

export interface TeamLabTopologyDetail {
  id: string
  revision: number
  schemaVersion: number
  definition: TeamLabTopologyDefinition
  editor: TeamLabTopologyEditor
  createdAt: number
  updatedAt: number
}

export interface TeamLabTopologySummary {
  id: string
  name: string
  revision: number
  schemaVersion: number
  createdAt: number
  updatedAt: number
}

export interface TeamLabAdminReleaseSummary {
  id: string
  version: number
  sourceRevision: number
  contentHash: string
  publishedAt: number
}

export interface TeamLabAdminValidationSummary {
  revision: number
  valid: boolean
  issueCount: number
  validatedAt: number
}

export interface TeamLabAdminRuntimeSummary {
  id: string
  releaseId: string
  status: TeamLabRuntimeStatus
  stage: string
  openForAccess: boolean
  createdAt: number
  updatedAt: number | null
  error: string | null
}

export interface TeamLabAdminSceneSummary {
  id: string
  name: string
  ownerId: string | null
  ownerDisplayName: string
  revision: number
  schemaVersion: number
  networkCount: number
  assetCount: number
  infrastructureCount: number
  latestRelease: TeamLabAdminReleaseSummary | null
  validation: TeamLabAdminValidationSummary | null
  latestTrialRuntime: TeamLabAdminRuntimeSummary | null
  gameReferenceCount: number
  createdAt: number
  updatedAt: number
}

export interface TeamLabAdminScenePage {
  items: readonly TeamLabAdminSceneSummary[]
  nextCursor: string | null
}

export interface TeamLabAdminRuntimePage {
  items: readonly TeamLabAdminRuntimeSummary[]
  nextCursor: string | null
}

export interface TeamLabValidationIssue {
  code: string
  path: string
  message: string
}

export interface TeamLabValidationResult {
  valid: boolean
  issues: readonly TeamLabValidationIssue[]
}

export interface TeamLabRelease {
  id: string
  topologyId: string
  version: number
  sourceRevision: number
  schemaVersion: number
  contentHash: string
  publishedBy: string | null
  publisherName: string | null
  publishedAt: number
}

export interface TeamLabPlan {
  topologyId: string
  releaseId: string
  networks: readonly {
    key: string
    name: string
    candidateCidr: string
    isEntry: boolean
  }[]
  assets: readonly {
    key: string
    name: string
    kind: TeamLabAssetKind
    imageTemplateId: number
    resources: TeamLabAssetResources
    interfaces: readonly Omit<TeamLabTopologyInterface, 'orderIndex'>[]
  }[]
  shards: readonly {
    key: string
    networkKeys: readonly string[]
    assetKeys: readonly string[]
    dockerSlots: number
    vmSlots: number
    infrastructureKeys: readonly string[]
  }[]
  crossShardConnections: number
  requiredCapabilities: readonly string[]
  warnings: readonly string[]
  planHash: string
  managedInfrastructureCount: number
  observationPointEstimate: number
}

export interface TeamLabAdminImageReadiness {
  imageTemplateId: number
  name: string
  imageType: TeamLabImageType
  eligibleNodeCount: number
  readyNodeCount: number
  pendingNodeCount: number
  failedNodeCount: number
}

export interface TeamLabAdminReleaseReadiness {
  topologyId: string
  releaseId: string
  ready: boolean
  plan: TeamLabPlan | null
  images: readonly TeamLabAdminImageReadiness[]
  latestTrialRuntime: TeamLabAdminRuntimeSummary | null
  blockingReasons: readonly string[]
}

export interface TeamLabCapabilities {
  apiVersion: string
  topologySchemaVersions: readonly number[]
  assetKinds: readonly TeamLabAssetKind[]
  networkModel: string
  features: {
    multiNode: boolean
    linuxVm: boolean
    windowsVm: boolean
    trafficFlows: boolean
    onDemandPcap: boolean
  }
  limits: {
    networksPerTopology: number
    assetsPerTopology: number
    interfacesPerAsset: number
  }
}

export interface CreateTeamLabTopologyRequest extends TeamLabTopologyDefinition {
  schemaVersion: 2
  editor: TeamLabTopologyEditor
}

export interface UpdateTeamLabTopologyRequest extends CreateTeamLabTopologyRequest {
  revision: number
}

export interface PublishTeamLabTopologyRequest {
  revision: number
}
