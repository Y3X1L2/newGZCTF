import type { TeamLabAssetKind, TeamLabRuntimeStatus } from './teamlabContracts'
import type {
  TeamLabAccessGrant,
  TeamLabCapture,
  TeamLabCaptureSegmentStatus,
  TeamLabCaptureStatus,
  TeamLabEventLevel,
  TeamLabObservationPointKind,
  TeamLabPathConfidence,
  TeamLabRuntime,
  TeamLabRuntimeEvent,
  TeamLabTrafficEvidenceKind,
  TeamLabTrafficFlowPage,
  TeamLabTrafficPath,
  TeamLabTrafficPathPage,
} from './teamlabRuntimeContracts'
import { teamLabParsing as parse } from './teamlabParsers'

const assetKinds = { 0: 'docker', 1: 'vm', Docker: 'docker', Vm: 'vm' } as const
const runtimeStatuses = {
  0: 'pending',
  1: 'planning',
  2: 'scheduled',
  3: 'deploying',
  4: 'probing',
  5: 'running',
  6: 'failed',
  7: 'cleanup-pending',
  8: 'stopped',
  9: 'destroying',
  10: 'destroyed',
  Pending: 'pending',
  Planning: 'planning',
  Scheduled: 'scheduled',
  Deploying: 'deploying',
  Probing: 'probing',
  Running: 'running',
  Failed: 'failed',
  CleanupPending: 'cleanup-pending',
  Stopped: 'stopped',
  Destroying: 'destroying',
  Destroyed: 'destroyed',
} as const
const eventLevels = {
  0: 'info',
  1: 'success',
  2: 'warning',
  3: 'error',
  Info: 'info',
  Success: 'success',
  Warning: 'warning',
  Error: 'error',
} as const
const pathConfidences = {
  0: 'packet-exact',
  1: 'process-correlated',
  2: 'temporally-related',
  PacketExact: 'packet-exact',
  ProcessCorrelated: 'process-correlated',
  TemporallyRelated: 'temporally-related',
} as const
const evidenceKinds = {
  0: 'packet',
  1: 'endpoint-process',
  Packet: 'packet',
  EndpointProcess: 'endpoint-process',
} as const
const observationPointKinds = {
  0: 'network-bridge',
  1: 'router-fragment',
  2: 'fabric-uplink',
  3: 'workload-endpoint',
  NetworkBridge: 'network-bridge',
  RouterFragment: 'router-fragment',
  FabricUplink: 'fabric-uplink',
  WorkloadEndpoint: 'workload-endpoint',
} as const
const captureStatuses = {
  0: 'pending',
  1: 'running',
  2: 'stopping',
  3: 'completed',
  4: 'failed',
  5: 'expired',
  6: 'cleanup-pending',
  Pending: 'pending',
  Running: 'running',
  Stopping: 'stopping',
  Completed: 'completed',
  Failed: 'failed',
  Expired: 'expired',
  CleanupPending: 'cleanup-pending',
} as const
const captureSegmentStatuses = {
  0: 'pending',
  1: 'running',
  2: 'stopping',
  3: 'captured',
  4: 'uploading',
  5: 'uploaded',
  6: 'failed',
  7: 'expired',
  8: 'cleanup-pending',
  Pending: 'pending',
  Running: 'running',
  Stopping: 'stopping',
  Captured: 'captured',
  Uploading: 'uploading',
  Uploaded: 'uploaded',
  Failed: 'failed',
  Expired: 'expired',
  CleanupPending: 'cleanup-pending',
} as const

function runtimeStatus(value: unknown, label: string): TeamLabRuntimeStatus {
  return parse.enumValue(value, runtimeStatuses, label)
}

function assetKind(value: unknown, label: string): TeamLabAssetKind {
  return parse.enumValue(value, assetKinds, label)
}

export function parseTeamLabRuntime(value: unknown): TeamLabRuntime {
  const item = parse.record(value, 'TeamLab runtime')
  return {
    id: parse.string(item.id, 'TeamLab runtime.id'),
    releaseId: parse.string(item.releaseId, 'TeamLab runtime.releaseId'),
    generation: parse.number(item.generation, 'TeamLab runtime.generation'),
    status: runtimeStatus(item.status, 'TeamLab runtime.status'),
    stage: parse.string(item.stage, 'TeamLab runtime.stage'),
    openForAccess: parse.boolean(item.openForAccess, 'TeamLab runtime.openForAccess'),
    shards: parse.array(item.shards, 'TeamLab runtime.shards', (entry, label) => {
      const shard = parse.record(entry, label)
      return {
        id: parse.string(shard.id, `${label}.id`),
        workerNodeId: parse.string(shard.workerNodeId, `${label}.workerNodeId`),
        workerNodeName: parse.string(shard.workerNodeName, `${label}.workerNodeName`),
        status: runtimeStatus(shard.status, `${label}.status`),
        networkKeys: parse.array(shard.networkKeys, `${label}.networkKeys`, parse.string),
        assetKeys: parse.array(shard.assetKeys, `${label}.assetKeys`, parse.string),
        error: parse.nullableString(shard.error, `${label}.error`),
      }
    }),
    networks: parse.array(item.networks, 'TeamLab runtime.networks', (entry, label) => {
      const network = parse.record(entry, label)
      return {
        key: parse.string(network.key, `${label}.key`),
        name: parse.string(network.name, `${label}.name`),
        cidr: parse.string(network.cidr, `${label}.cidr`),
        gatewayIp: parse.string(network.gatewayIp, `${label}.gatewayIp`),
      }
    }),
    assets: parse.array(item.assets, 'TeamLab runtime.assets', (entry, label) => {
      const asset = parse.record(entry, label)
      return {
        id: parse.number(asset.id, `${label}.id`),
        key: parse.string(asset.key, `${label}.key`),
        name: parse.string(asset.name, `${label}.name`),
        kind: assetKind(asset.kind, `${label}.kind`),
        runtimeResourceId: parse.nullableString(asset.runtimeResourceId, `${label}.runtimeResourceId`),
        primaryIp: parse.nullableString(asset.primaryIp, `${label}.primaryIp`),
        status: runtimeStatus(asset.status, `${label}.status`),
        error: parse.nullableString(asset.error, `${label}.error`),
      }
    }),
    createdAt: parse.number(item.createdAt, 'TeamLab runtime.createdAt'),
    updatedAt: parse.nullableNumber(item.updatedAt, 'TeamLab runtime.updatedAt'),
    error: parse.nullableString(item.error, 'TeamLab runtime.error'),
  }
}

export function parseTeamLabRuntimeEvents(value: unknown): readonly TeamLabRuntimeEvent[] {
  return parse.array(value, 'TeamLab runtime events', (entry, label) => {
    const item = parse.record(entry, label)
    return {
      cursor: parse.number(item.cursor, `${label}.cursor`),
      generation: parse.number(item.generation, `${label}.generation`),
      stage: parse.string(item.stage, `${label}.stage`),
      level: parse.enumValue<TeamLabEventLevel>(item.level, eventLevels, `${label}.level`),
      message: parse.string(item.message, `${label}.message`),
      objectType: parse.nullableString(item.objectType, `${label}.objectType`),
      objectId: parse.nullableString(item.objectId, `${label}.objectId`),
      createdAt: parse.number(item.createdAt, `${label}.createdAt`),
    }
  })
}

export function parseTeamLabAccessGrant(value: unknown): TeamLabAccessGrant {
  const item = parse.record(value, 'TeamLab access grant')
  const type = parse.enumValue(item.type, { WireGuard: 'WireGuard' }, 'TeamLab access grant.type')
  return {
    id: parse.string(item.id, 'TeamLab access grant.id'),
    type,
    clientAddress: parse.string(item.clientAddress, 'TeamLab access grant.clientAddress'),
    endpoint: parse.string(item.endpoint, 'TeamLab access grant.endpoint'),
    allowedIps: parse.string(item.allowedIps, 'TeamLab access grant.allowedIps'),
    dns: parse.string(item.dns, 'TeamLab access grant.dns'),
    createdAt: parse.number(item.createdAt, 'TeamLab access grant.createdAt'),
    expiresAt: parse.nullableNumber(item.expiresAt, 'TeamLab access grant.expiresAt'),
    configurationDownloadUrl: parse.nullableString(
      item.configurationDownloadUrl,
      'TeamLab access grant.configurationDownloadUrl'
    ),
  }
}

export function parseTeamLabAccessGrants(value: unknown): readonly TeamLabAccessGrant[] {
  return parse.array(value, 'TeamLab access grants', (entry) => parseTeamLabAccessGrant(entry))
}

export function parseTeamLabTrafficFlowPage(value: unknown): TeamLabTrafficFlowPage {
  const page = parse.record(value, 'TeamLab traffic flow page')
  return {
    items: parse.array(page.items, 'TeamLab traffic flow page.items', (entry, label) => {
      const item = parse.record(entry, label)
      return {
        cursor: parse.string(item.cursor, `${label}.cursor`),
        shardId: parse.string(item.shardId, `${label}.shardId`),
        networkKey: parse.string(item.networkKey, `${label}.networkKey`),
        sourceIp: parse.string(item.sourceIp, `${label}.sourceIp`),
        sourcePort: parse.nullableNumber(item.sourcePort, `${label}.sourcePort`),
        destinationIp: parse.string(item.destinationIp, `${label}.destinationIp`),
        destinationPort: parse.nullableNumber(item.destinationPort, `${label}.destinationPort`),
        protocol: parse.string(item.protocol, `${label}.protocol`),
        bytes: parse.number(item.bytes, `${label}.bytes`),
        packets: parse.number(item.packets, `${label}.packets`),
        firstSeen: parse.number(item.firstSeen, `${label}.firstSeen`),
        lastSeen: parse.number(item.lastSeen, `${label}.lastSeen`),
      }
    }),
    nextCursor: parse.nullableString(page.nextCursor, 'TeamLab traffic flow page.nextCursor'),
  }
}

function parsePathIdentity(item: Record<string, unknown>, label: string) {
  return {
    sourceIp: parse.string(item.sourceIp, `${label}.sourceIp`),
    sourcePort: parse.nullableNumber(item.sourcePort, `${label}.sourcePort`),
    destinationIp: parse.string(item.destinationIp, `${label}.destinationIp`),
    destinationPort: parse.nullableNumber(item.destinationPort, `${label}.destinationPort`),
    protocol: parse.string(item.protocol, `${label}.protocol`),
    startedAt: parse.number(item.startedAt, `${label}.startedAt`),
    endedAt: parse.number(item.endedAt, `${label}.endedAt`),
  }
}

export function parseTeamLabTrafficPathPage(value: unknown): TeamLabTrafficPathPage {
  const page = parse.record(value, 'TeamLab traffic path page')
  return {
    items: parse.array(page.items, 'TeamLab traffic path page.items', (entry, label) => {
      const item = parse.record(entry, label)
      return {
        cursor: parse.string(item.cursor, `${label}.cursor`),
        id: parse.string(item.id, `${label}.id`),
        confidence: parse.enumValue<TeamLabPathConfidence>(item.confidence, pathConfidences, `${label}.confidence`),
        ...parsePathIdentity(item, label),
        hopCount: parse.number(item.hopCount, `${label}.hopCount`),
      }
    }),
    nextCursor: parse.nullableString(page.nextCursor, 'TeamLab traffic path page.nextCursor'),
  }
}

export function parseTeamLabTrafficPath(value: unknown): TeamLabTrafficPath {
  const item = parse.record(value, 'TeamLab traffic path')
  return {
    id: parse.string(item.id, 'TeamLab traffic path.id'),
    confidence: parse.enumValue<TeamLabPathConfidence>(
      item.confidence,
      pathConfidences,
      'TeamLab traffic path.confidence'
    ),
    ...parsePathIdentity(item, 'TeamLab traffic path'),
    hops: parse.array(item.hops, 'TeamLab traffic path.hops', (entry, label) => {
      const hop = parse.record(entry, label)
      return {
        ordinal: parse.number(hop.ordinal, `${label}.ordinal`),
        observedAt: parse.number(hop.observedAt, `${label}.observedAt`),
        evidenceKind: parse.enumValue<TeamLabTrafficEvidenceKind>(
          hop.evidenceKind,
          evidenceKinds,
          `${label}.evidenceKind`
        ),
        observationPointKind: parse.enumValue<TeamLabObservationPointKind>(
          hop.observationPointKind,
          observationPointKinds,
          `${label}.observationPointKind`
        ),
        shardId: parse.nullableString(hop.shardId, `${label}.shardId`),
        networkKey: parse.nullableString(hop.networkKey, `${label}.networkKey`),
        infrastructureKey: parse.nullableString(hop.infrastructureKey, `${label}.infrastructureKey`),
        assetKey: parse.nullableString(hop.assetKey, `${label}.assetKey`),
        direction: parse.string(hop.direction, `${label}.direction`),
        sourceIp: parse.string(hop.sourceIp, `${label}.sourceIp`),
        sourcePort: parse.nullableNumber(hop.sourcePort, `${label}.sourcePort`),
        destinationIp: parse.string(hop.destinationIp, `${label}.destinationIp`),
        destinationPort: parse.nullableNumber(hop.destinationPort, `${label}.destinationPort`),
        protocol: parse.string(hop.protocol, `${label}.protocol`),
      }
    }),
  }
}

export function parseTeamLabCapture(value: unknown): TeamLabCapture {
  const item = parse.record(value, 'TeamLab capture')
  return {
    id: parse.string(item.id, 'TeamLab capture.id'),
    status: parse.enumValue<TeamLabCaptureStatus>(item.status, captureStatuses, 'TeamLab capture.status'),
    scope: parse.string(item.scope, 'TeamLab capture.scope'),
    networkKey: parse.nullableString(item.networkKey, 'TeamLab capture.networkKey'),
    maxBytes: parse.number(item.maxBytes, 'TeamLab capture.maxBytes'),
    maxSeconds: parse.number(item.maxSeconds, 'TeamLab capture.maxSeconds'),
    capturedBytes: parse.number(item.capturedBytes, 'TeamLab capture.capturedBytes'),
    createdAt: parse.number(item.createdAt, 'TeamLab capture.createdAt'),
    startedAt: parse.nullableNumber(item.startedAt, 'TeamLab capture.startedAt'),
    completedAt: parse.nullableNumber(item.completedAt, 'TeamLab capture.completedAt'),
    expiresAt: parse.nullableNumber(item.expiresAt, 'TeamLab capture.expiresAt'),
    segments: parse.array(item.segments, 'TeamLab capture.segments', (entry, label) => {
      const segment = parse.record(entry, label)
      return {
        id: parse.string(segment.id, `${label}.id`),
        status: parse.enumValue<TeamLabCaptureSegmentStatus>(
          segment.status,
          captureSegmentStatuses,
          `${label}.status`
        ),
        observationPointId: parse.string(segment.observationPointId, `${label}.observationPointId`),
        observationPointKind: parse.enumValue<TeamLabObservationPointKind>(
          segment.observationPointKind,
          observationPointKinds,
          `${label}.observationPointKind`
        ),
        networkKey: parse.nullableString(segment.networkKey, `${label}.networkKey`),
        infrastructureKey: parse.nullableString(segment.infrastructureKey, `${label}.infrastructureKey`),
        assetKey: parse.nullableString(segment.assetKey, `${label}.assetKey`),
        capturedBytes: parse.number(segment.capturedBytes, `${label}.capturedBytes`),
        uploadedBytes: parse.number(segment.uploadedBytes, `${label}.uploadedBytes`),
        sha256: parse.nullableString(segment.sha256, `${label}.sha256`),
        error: parse.nullableString(segment.error, `${label}.error`),
      }
    }),
    error: parse.nullableString(item.error, 'TeamLab capture.error'),
  }
}
