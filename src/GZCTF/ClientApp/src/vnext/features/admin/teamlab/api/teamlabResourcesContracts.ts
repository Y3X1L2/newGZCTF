export type TeamLabDeviceArtifactKind = 'oci-image' | 'vm-image'

export interface TeamLabDevicePackagePort {
  name: string
  port: number
  protocol: string
}

export interface TeamLabDevicePackage {
  id: string
  name: string
  displayName: string
  version: string
  artifactKind: TeamLabDeviceArtifactKind
  artifactReference: string
  digest: string | null
  description: string | null
  supportedAssetKinds: readonly string[]
  cpuMillis: number
  memoryMiB: number
  storageGib: number
  ports: readonly TeamLabDevicePackagePort[]
  parameterSchema: unknown
  healthDeclaration: unknown
  protocolEventTypes: readonly string[]
  enabled: boolean
  archived: boolean
  createdAt: string
  updatedAt: string
}

export interface TeamLabDevicePackagePage {
  items: readonly TeamLabDevicePackage[]
  next: string | null
}

export interface RegisterTeamLabDevicePackageRequest {
  name: string
  displayName: string
  version: string
  artifactKind: TeamLabDeviceArtifactKind
  artifactReference: string
  digest?: string | null
  description?: string | null
  supportedAssetKinds: readonly string[]
  cpuMillis: number
  memoryMiB: number
  storageGib: number
  ports?: readonly TeamLabDevicePackagePort[]
  parameterSchema?: unknown
  healthDeclaration?: unknown
  protocolEventTypes?: readonly string[]
}

export type TeamLabConnectorKind =
  | 'managed-nic'
  | 'vlan'
  | 'segment'
  | 'serial'
  | 'usb-gateway'
  | 'dedicated-network'

export type TeamLabConnectorHealth = 'unknown' | 'healthy' | 'degraded' | 'unreachable'

export type TeamLabConnectorReleaseReason =
  | 'none'
  | 'manual-release'
  | 'runtime-destroyed'
  | 'admin-revoked'
  | 'node-lost'

export interface TeamLabConnectorLease {
  id: string
  connectorId: string
  runtimeId: string
  slot: number
  acquiredAt: string
  releasedAt: string | null
  releaseReason: TeamLabConnectorReleaseReason
}

export interface TeamLabConnector {
  id: string
  name: string
  displayName: string
  kind: TeamLabConnectorKind
  controlScopeId: string | null
  supportsSharedUse: boolean
  capacity: number
  occupiedSlots: number
  activeLeases: readonly TeamLabConnectorLease[]
  health: TeamLabConnectorHealth
  healthObservedAt: string | null
  description: string | null
  archived: boolean
  createdAt: string
  updatedAt: string
}

export interface TeamLabConnectorPage {
  items: readonly TeamLabConnector[]
  next: string | null
}

export interface RegisterTeamLabConnectorRequest {
  name: string
  displayName: string
  kind: TeamLabConnectorKind
  controlScopeId?: string | null
  supportsSharedUse: boolean
  capacity: number
  attachmentReference?: string | null
  description?: string | null
}

export interface TeamLabNodeCacheEntry {
  templateId: number
  nodeId: string
  imageHash: string | null
  status: string
  operation: string
  stage: string
  attemptCount: number
  activeReferenceCount: number
  lastErrorCode: string | null
  progressUpdatedAt: string | null
}

export interface TeamLabNodeCachePage {
  items: readonly TeamLabNodeCacheEntry[]
  next: string | null
}

export type TeamLabLinkPolicyKind =
  | 'access-rule'
  | 'nat'
  | 'bandwidth-limit'
  | 'latency'
  | 'jitter'
  | 'packet-loss'
  | 'duplication'
  | 'link-break'

export type TeamLabLinkPolicyStatus = 'active' | 'recovered' | 'failed'

export type TeamLabLinkPolicyRecoverOrigin = 'none' | 'scheduled' | 'manual' | 'runtime-destroyed'

export interface TeamLabLinkPolicy {
  id: string
  runtimeId: string
  networkKey: string
  assetKey: string | null
  kind: TeamLabLinkPolicyKind
  parameters: unknown
  status: TeamLabLinkPolicyStatus
  recoverAt: string | null
  appliedAt: string
  recoveredAt: string | null
  recoverOrigin: TeamLabLinkPolicyRecoverOrigin
  lastError: string | null
}

export interface TeamLabLinkPolicyPage {
  items: readonly TeamLabLinkPolicy[]
  next: string | null
}

export interface ApplyTeamLabLinkPolicyRequest {
  runtimeId: string
  networkKey: string
  assetKey?: string | null
  kind: TeamLabLinkPolicyKind
  parameters?: unknown
  recoverAt?: string | null
}
