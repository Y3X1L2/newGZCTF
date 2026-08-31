import type { TeamLabAssetKind, TeamLabRuntimeStatus } from './teamlabContracts'

export type TeamLabEventLevel = 'info' | 'success' | 'warning' | 'error'
export type TeamLabPathConfidence = 'packet-exact' | 'process-correlated' | 'temporally-related'
export type TeamLabTrafficEvidenceKind = 'packet' | 'endpoint-process'
export type TeamLabObservationPointKind =
  | 'network-bridge'
  | 'router-fragment'
  | 'fabric-uplink'
  | 'workload-endpoint'
export type TeamLabCaptureStatus =
  | 'pending'
  | 'running'
  | 'stopping'
  | 'completed'
  | 'failed'
  | 'expired'
  | 'cleanup-pending'
export type TeamLabCaptureSegmentStatus =
  | 'pending'
  | 'running'
  | 'stopping'
  | 'captured'
  | 'uploading'
  | 'uploaded'
  | 'failed'
  | 'expired'
  | 'cleanup-pending'

export interface TeamLabRuntimeShard {
  id: string
  workerNodeId: string
  workerNodeName: string
  status: TeamLabRuntimeStatus
  networkKeys: readonly string[]
  assetKeys: readonly string[]
  error: string | null
}

export interface TeamLabRuntimeNetwork {
  key: string
  name: string
  cidr: string
  gatewayIp: string
}

export interface TeamLabRuntimeAsset {
  id: number
  key: string
  name: string
  kind: TeamLabAssetKind
  runtimeResourceId: string | null
  primaryIp: string | null
  status: TeamLabRuntimeStatus
  error: string | null
}

export interface TeamLabRuntime {
  id: string
  releaseId: string
  generation: number
  status: TeamLabRuntimeStatus
  stage: string
  openForAccess: boolean
  shards: readonly TeamLabRuntimeShard[]
  networks: readonly TeamLabRuntimeNetwork[]
  assets: readonly TeamLabRuntimeAsset[]
  createdAt: number
  updatedAt: number | null
  error: string | null
}

export interface TeamLabRuntimeEvent {
  cursor: number
  generation: number
  stage: string
  level: TeamLabEventLevel
  message: string
  objectType: string | null
  objectId: string | null
  createdAt: number
}

export interface TeamLabRuntimeConstraints {
  preferredRegion: string | null
  requiredCapabilities: readonly string[]
}

export interface TeamLabRuntimeOverlay {
  assetKey: string
  secrets: Readonly<Record<string, string>> | null
}

export interface CreateTeamLabTrialRequest {
  releaseId: string
  constraints: TeamLabRuntimeConstraints | null
  overlays: readonly TeamLabRuntimeOverlay[] | null
  externalReference: string | null
}

export interface ResetTeamLabRuntimeRequest {
  overlays: readonly TeamLabRuntimeOverlay[] | null
  releaseId: string | null
}

export interface TeamLabAccessGrant {
  id: string
  type: 'WireGuard'
  clientAddress: string
  endpoint: string
  allowedIps: string
  dns: string
  createdAt: number
  expiresAt: number | null
  configurationDownloadUrl: string | null
}

export interface TeamLabTrafficFlow {
  cursor: string
  shardId: string
  networkKey: string
  sourceIp: string
  sourcePort: number | null
  destinationIp: string
  destinationPort: number | null
  protocol: string
  bytes: number
  packets: number
  firstSeen: number
  lastSeen: number
}

export interface TeamLabTrafficCompleteness {
  complete: boolean
  droppedRecords: number
}

export interface TeamLabTrafficFlowPage {
  items: readonly TeamLabTrafficFlow[]
  nextCursor: string | null
  completeness: TeamLabTrafficCompleteness
}

export interface TeamLabTrafficPathSummary {
  cursor: string
  id: string
  confidence: TeamLabPathConfidence
  sourceIp: string
  sourcePort: number | null
  destinationIp: string
  destinationPort: number | null
  protocol: string
  startedAt: number
  endedAt: number
  hopCount: number
}

export interface TeamLabTrafficPathPage {
  items: readonly TeamLabTrafficPathSummary[]
  nextCursor: string | null
  completeness: TeamLabTrafficCompleteness
}

export interface TeamLabTrafficPathHop {
  ordinal: number
  observedAt: number
  evidenceKind: TeamLabTrafficEvidenceKind
  observationPointKind: TeamLabObservationPointKind
  shardId: string | null
  networkKey: string | null
  infrastructureKey: string | null
  assetKey: string | null
  direction: string
  sourceIp: string
  sourcePort: number | null
  destinationIp: string
  destinationPort: number | null
  protocol: string
}

export interface TeamLabTrafficPath {
  id: string
  confidence: TeamLabPathConfidence
  sourceIp: string
  sourcePort: number | null
  destinationIp: string
  destinationPort: number | null
  protocol: string
  startedAt: number
  endedAt: number
  hops: readonly TeamLabTrafficPathHop[]
}

export interface CreateTeamLabCaptureRequest {
  scope: string
  networkKey: string | null
  maxSeconds: number
  maxBytes: number
  expiresInSeconds: number
}

export interface TeamLabCaptureSegment {
  id: string
  status: TeamLabCaptureSegmentStatus
  observationPointId: string
  observationPointKind: TeamLabObservationPointKind
  networkKey: string | null
  infrastructureKey: string | null
  assetKey: string | null
  capturedBytes: number
  uploadedBytes: number
  sha256: string | null
  error: string | null
}

export interface TeamLabCapture {
  id: string
  status: TeamLabCaptureStatus
  scope: string
  networkKey: string | null
  maxBytes: number
  maxSeconds: number
  capturedBytes: number
  createdAt: number
  startedAt: number | null
  completedAt: number | null
  expiresAt: number | null
  segments: readonly TeamLabCaptureSegment[]
  error: string | null
}
