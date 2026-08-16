import type {
  TeamLabConnector,
  TeamLabConnectorHealth,
  TeamLabConnectorKind,
  TeamLabConnectorLease,
  TeamLabConnectorPage,
  TeamLabConnectorReleaseReason,
  TeamLabDeviceArtifactKind,
  TeamLabDevicePackage,
  TeamLabDevicePackagePage,
  TeamLabDevicePackagePort,
  TeamLabLinkPolicy,
  TeamLabLinkPolicyKind,
  TeamLabLinkPolicyPage,
  TeamLabLinkPolicyRecoverOrigin,
  TeamLabLinkPolicyStatus,
  TeamLabNodeCacheEntry,
  TeamLabNodeCachePage,
} from './teamlabResourcesContracts'
import { teamLabParsing as parse } from './teamlabParsers'

const artifactKinds = { 'oci-image': 'oci-image', 'vm-image': 'vm-image' } as const
const connectorKinds = {
  'managed-nic': 'managed-nic',
  vlan: 'vlan',
  segment: 'segment',
  serial: 'serial',
  'usb-gateway': 'usb-gateway',
  'dedicated-network': 'dedicated-network',
} as const
const connectorHealths = {
  unknown: 'unknown',
  healthy: 'healthy',
  degraded: 'degraded',
  unreachable: 'unreachable',
} as const
const releaseReasons = {
  none: 'none',
  'manual-release': 'manual-release',
  'runtime-destroyed': 'runtime-destroyed',
  'admin-revoked': 'admin-revoked',
  'node-lost': 'node-lost',
} as const
const linkPolicyKinds = {
  'access-rule': 'access-rule',
  nat: 'nat',
  'bandwidth-limit': 'bandwidth-limit',
  latency: 'latency',
  jitter: 'jitter',
  'packet-loss': 'packet-loss',
  duplication: 'duplication',
  'link-break': 'link-break',
} as const
const linkPolicyStatuses = { active: 'active', recovered: 'recovered', failed: 'failed' } as const
const recoverOrigins = {
  none: 'none',
  scheduled: 'scheduled',
  manual: 'manual',
  'runtime-destroyed': 'runtime-destroyed',
} as const

function parseDevicePort(value: unknown, label: string): TeamLabDevicePackagePort {
  const item = parse.record(value, label)
  return {
    name: parse.string(item.name, `${label}.name`),
    port: parse.number(item.port, `${label}.port`),
    protocol: parse.string(item.protocol, `${label}.protocol`),
  }
}

export function parseTeamLabDevicePackage(value: unknown, label = 'devicePackage'): TeamLabDevicePackage {
  const item = parse.record(value, label)
  return {
    id: parse.string(item.id, `${label}.id`),
    name: parse.string(item.name, `${label}.name`),
    displayName: parse.string(item.displayName, `${label}.displayName`),
    version: parse.string(item.version, `${label}.version`),
    artifactKind: parse.enumValue(
      item.artifactKind,
      artifactKinds as Record<string, TeamLabDeviceArtifactKind>,
      `${label}.artifactKind`
    ),
    artifactReference: parse.string(item.artifactReference, `${label}.artifactReference`),
    digest: parse.nullableString(item.digest, `${label}.digest`),
    description: parse.nullableString(item.description, `${label}.description`),
    supportedAssetKinds: parse
      .array(item.supportedAssetKinds ?? [], `${label}.supportedAssetKinds`, (entry, entryLabel) =>
        parse.string(entry, entryLabel)
      ),
    cpuMillis: parse.number(item.cpuMillis, `${label}.cpuMillis`),
    memoryMiB: parse.number(item.memoryMiB, `${label}.memoryMiB`),
    storageGib: parse.number(item.storageGib, `${label}.storageGib`),
    ports: parse.array(item.ports ?? [], `${label}.ports`, parseDevicePort),
    parameterSchema: item.parameterSchema ?? null,
    healthDeclaration: item.healthDeclaration ?? null,
    protocolEventTypes: parse.array(item.protocolEventTypes ?? [], `${label}.protocolEventTypes`, (entry, entryLabel) =>
      parse.string(entry, entryLabel)
    ),
    enabled: parse.boolean(item.enabled, `${label}.enabled`),
    archived: parse.boolean(item.archived, `${label}.archived`),
    createdAt: parse.string(item.createdAt, `${label}.createdAt`),
    updatedAt: parse.string(item.updatedAt, `${label}.updatedAt`),
  }
}

export function parseTeamLabDevicePackagePage(value: unknown, label = 'devicePackagePage'): TeamLabDevicePackagePage {
  const item = parse.record(value, label)
  return {
    items: parse.array(item.items, `${label}.items`, parseTeamLabDevicePackage),
    next: parse.nullableString(item.next, `${label}.next`),
  }
}

function parseConnectorLease(value: unknown, label: string): TeamLabConnectorLease {
  const item = parse.record(value, label)
  return {
    id: parse.string(item.id, `${label}.id`),
    connectorId: parse.string(item.connectorId, `${label}.connectorId`),
    runtimeId: parse.string(item.runtimeId, `${label}.runtimeId`),
    slot: parse.number(item.slot, `${label}.slot`),
    acquiredAt: parse.string(item.acquiredAt, `${label}.acquiredAt`),
    releasedAt: parse.nullableString(item.releasedAt, `${label}.releasedAt`),
    releaseReason: parse.enumValue(
      item.releaseReason,
      releaseReasons as Record<string, TeamLabConnectorReleaseReason>,
      `${label}.releaseReason`
    ),
  }
}

export function parseTeamLabConnector(value: unknown, label = 'connector'): TeamLabConnector {
  const item = parse.record(value, label)
  return {
    id: parse.string(item.id, `${label}.id`),
    name: parse.string(item.name, `${label}.name`),
    displayName: parse.string(item.displayName, `${label}.displayName`),
    kind: parse.enumValue(item.kind, connectorKinds as Record<string, TeamLabConnectorKind>, `${label}.kind`),
    controlScopeId: parse.nullableString(item.controlScopeId, `${label}.controlScopeId`),
    supportsSharedUse: parse.boolean(item.supportsSharedUse, `${label}.supportsSharedUse`),
    capacity: parse.number(item.capacity, `${label}.capacity`),
    occupiedSlots: parse.number(item.occupiedSlots, `${label}.occupiedSlots`),
    activeLeases: parse.array(item.activeLeases ?? [], `${label}.activeLeases`, parseConnectorLease),
    health: parse.enumValue(item.health, connectorHealths as Record<string, TeamLabConnectorHealth>, `${label}.health`),
    healthObservedAt: parse.nullableString(item.healthObservedAt, `${label}.healthObservedAt`),
    description: parse.nullableString(item.description, `${label}.description`),
    archived: parse.boolean(item.archived, `${label}.archived`),
    createdAt: parse.string(item.createdAt, `${label}.createdAt`),
    updatedAt: parse.string(item.updatedAt, `${label}.updatedAt`),
  }
}

export function parseTeamLabConnectorPage(value: unknown, label = 'connectorPage'): TeamLabConnectorPage {
  const item = parse.record(value, label)
  return {
    items: parse.array(item.items, `${label}.items`, parseTeamLabConnector),
    next: parse.nullableString(item.next, `${label}.next`),
  }
}

function parseNodeCacheEntry(value: unknown, label: string): TeamLabNodeCacheEntry {
  const item = parse.record(value, label)
  return {
    templateId: parse.number(item.templateId, `${label}.templateId`),
    nodeId: parse.string(item.nodeId, `${label}.nodeId`),
    imageHash: parse.nullableString(item.imageHash, `${label}.imageHash`),
    status: parse.string(item.status, `${label}.status`),
    operation: parse.string(item.operation, `${label}.operation`),
    stage: parse.string(item.stage, `${label}.stage`),
    attemptCount: parse.number(item.attemptCount, `${label}.attemptCount`),
    activeReferenceCount: parse.number(item.activeReferenceCount, `${label}.activeReferenceCount`),
    lastErrorCode: parse.nullableString(item.lastErrorCode, `${label}.lastErrorCode`),
    progressUpdatedAt: parse.nullableString(item.progressUpdatedAt, `${label}.progressUpdatedAt`),
  }
}

export function parseTeamLabNodeCachePage(value: unknown, label = 'nodeCachePage'): TeamLabNodeCachePage {
  const item = parse.record(value, label)
  return {
    items: parse.array(item.items, `${label}.items`, parseNodeCacheEntry),
    next: parse.nullableString(item.next, `${label}.next`),
  }
}

export function parseTeamLabLinkPolicy(value: unknown, label = 'linkPolicy'): TeamLabLinkPolicy {
  const item = parse.record(value, label)
  return {
    id: parse.string(item.id, `${label}.id`),
    runtimeId: parse.string(item.runtimeId, `${label}.runtimeId`),
    networkKey: parse.string(item.networkKey, `${label}.networkKey`),
    assetKey: parse.nullableString(item.assetKey, `${label}.assetKey`),
    kind: parse.enumValue(item.kind, linkPolicyKinds as Record<string, TeamLabLinkPolicyKind>, `${label}.kind`),
    parameters: item.parameters ?? null,
    status: parse.enumValue(item.status, linkPolicyStatuses as Record<string, TeamLabLinkPolicyStatus>, `${label}.status`),
    recoverAt: parse.nullableString(item.recoverAt, `${label}.recoverAt`),
    appliedAt: parse.string(item.appliedAt, `${label}.appliedAt`),
    recoveredAt: parse.nullableString(item.recoveredAt, `${label}.recoveredAt`),
    recoverOrigin: parse.enumValue(
      item.recoverOrigin,
      recoverOrigins as Record<string, TeamLabLinkPolicyRecoverOrigin>,
      `${label}.recoverOrigin`
    ),
    lastError: parse.nullableString(item.lastError, `${label}.lastError`),
  }
}

export function parseTeamLabLinkPolicyPage(value: unknown, label = 'linkPolicyPage'): TeamLabLinkPolicyPage {
  const item = parse.record(value, label)
  return {
    items: parse.array(item.items, `${label}.items`, parseTeamLabLinkPolicy),
    next: parse.nullableString(item.next, `${label}.next`),
  }
}
