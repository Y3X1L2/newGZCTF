import type {
  CreateTeamLabTopologyRequest,
  TeamLabAdminReleaseReadiness,
  TeamLabAdminRuntimePage,
  TeamLabAdminRuntimeSummary,
  TeamLabAdminScenePage,
  TeamLabAssetKind,
  TeamLabBootstrapReference,
  TeamLabCapabilities,
  TeamLabConnectionDirection,
  TeamLabDependencyCondition,
  TeamLabEditorItem,
  TeamLabEndpointObservationMode,
  TeamLabHealthCheckKind,
  TeamLabInfrastructureKind,
  TeamLabObservationPolicy,
  TeamLabPlan,
  TeamLabRelease,
  TeamLabTopologyAsset,
  TeamLabTopologyConnection,
  TeamLabTopologyDefinition,
  TeamLabTopologyDependency,
  TeamLabTopologyDetail,
  TeamLabTopologyEditor,
  TeamLabTopologyInfrastructure,
  TeamLabTopologyInterface,
  TeamLabTopologyNetwork,
  TeamLabTopologySummary,
  TeamLabValidationResult,
  UpdateTeamLabTopologyRequest,
} from './teamlabContracts'
import { teamLabContractFailure } from './teamlabErrors'

type UnknownRecord = Record<string, unknown>

const assetKinds = { 0: 'docker', 1: 'vm', Docker: 'docker', Vm: 'vm' } as const
const infrastructureKinds = {
  0: 'managed-switch',
  1: 'managed-router',
  ManagedSwitch: 'managed-switch',
  ManagedRouter: 'managed-router',
} as const
const directions = { 0: 'from-to', 1: 'bidirectional', FromTo: 'from-to', Bidirectional: 'bidirectional' } as const
const dependencyConditions = {
  0: 'network-ready',
  1: 'guest-ready',
  2: 'service-ready',
  3: 'bootstrap-completed',
  NetworkReady: 'network-ready',
  GuestReady: 'guest-ready',
  ServiceReady: 'service-ready',
  BootstrapCompleted: 'bootstrap-completed',
} as const
const observationModes = {
  0: 'disabled',
  1: 'optional',
  2: 'required',
  Disabled: 'disabled',
  Optional: 'optional',
  Required: 'required',
} as const
const healthKinds = { 0: 'tcp', 1: 'http', Tcp: 'tcp', Http: 'http' } as const
const imageTypes = {
  0: 'docker',
  1: 'qcow2',
  2: 'ova',
  3: 'vmdk',
  Docker: 'docker',
  Qcow2: 'qcow2',
  Ova: 'ova',
  Vmdk: 'vmdk',
} as const
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

function record(value: unknown, label: string): UnknownRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return teamLabContractFailure(label, value)
  return value as UnknownRecord
}

function string(value: unknown, label: string): string {
  if (typeof value !== 'string') return teamLabContractFailure(label, value)
  return value
}

function number(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return teamLabContractFailure(label, value)
  return value
}

function boolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') return teamLabContractFailure(label, value)
  return value
}

function nullableString(value: unknown, label: string): string | null {
  return value === null || value === undefined ? null : string(value, label)
}

function nullableNumber(value: unknown, label: string): number | null {
  return value === null || value === undefined ? null : number(value, label)
}

function array<T>(value: unknown, label: string, parser: (item: unknown, itemLabel: string) => T): T[] {
  if (!Array.isArray(value)) return teamLabContractFailure(label, value)
  return value.map((item, index) => parser(item, `${label}[${index}]`))
}

function optionalArray<T>(value: unknown, label: string, parser: (item: unknown, itemLabel: string) => T): T[] {
  return value === null || value === undefined ? [] : array(value, label, parser)
}

function stringRecord(value: unknown, label: string): Record<string, string> {
  const source = record(value, label)
  return Object.fromEntries(Object.entries(source).map(([key, item]) => [key, string(item, `${label}.${key}`)]))
}

function enumValue<T extends string>(value: unknown, values: Record<string, T>, label: string): T {
  const parsed = values[String(value)]
  if (!parsed) return teamLabContractFailure(label, value)
  return parsed
}

export const teamLabParsing = {
  record,
  string,
  number,
  boolean,
  nullableString,
  nullableNumber,
  array,
  enumValue,
}

function parseInterface(value: unknown, label: string): TeamLabTopologyInterface {
  const item = record(value, label)
  return {
    key: string(item.key, `${label}.key`),
    networkKey: string(item.networkKey, `${label}.networkKey`),
    hostOffset: number(item.hostOffset, `${label}.hostOffset`),
    primary: boolean(item.primary, `${label}.primary`),
    orderIndex: number(item.orderIndex ?? 0, `${label}.orderIndex`),
  }
}

function parseNetwork(value: unknown, label: string): TeamLabTopologyNetwork {
  const item = record(value, label)
  const pool = record(item.addressPool, `${label}.addressPool`)
  return {
    key: string(item.key, `${label}.key`),
    name: string(item.name, `${label}.name`),
    addressPool: {
      poolCidr: string(pool.poolCidr, `${label}.addressPool.poolCidr`),
      runtimePrefixLength: number(pool.runtimePrefixLength, `${label}.addressPool.runtimePrefixLength`),
    },
    isEntry: boolean(item.isEntry, `${label}.isEntry`),
    orderIndex: number(item.orderIndex ?? 0, `${label}.orderIndex`),
  }
}

function parseBootstrap(value: unknown, label: string): TeamLabBootstrapReference | null {
  if (value === null || value === undefined) return null
  const item = record(value, label)
  return {
    profileId: string(item.profileId, `${label}.profileId`),
    version: number(item.version, `${label}.version`),
    parameters: stringRecord(item.parameters, `${label}.parameters`),
  }
}

function parseAsset(value: unknown, label: string): TeamLabTopologyAsset {
  const item = record(value, label)
  const resources = record(item.resources, `${label}.resources`)
  const health =
    item.healthCheck === null || item.healthCheck === undefined
      ? null
      : record(item.healthCheck, `${label}.healthCheck`)
  return {
    key: string(item.key, `${label}.key`),
    name: string(item.name, `${label}.name`),
    kind: enumValue(item.kind, assetKinds, `${label}.kind`),
    imageTemplateId: number(item.imageTemplateId, `${label}.imageTemplateId`),
    resources: {
      cpuUnits: number(resources.cpuUnits, `${label}.resources.cpuUnits`),
      memoryMiB: number(resources.memoryMiB, `${label}.resources.memoryMiB`),
      storageMiB: number(resources.storageMiB, `${label}.resources.storageMiB`),
    },
    interfaces: array(item.interfaces, `${label}.interfaces`, parseInterface),
    routingEnabled: boolean(item.routingEnabled, `${label}.routingEnabled`),
    exposePort: nullableNumber(item.exposePort, `${label}.exposePort`),
    environment:
      item.environment === null || item.environment === undefined
        ? null
        : stringRecord(item.environment, `${label}.environment`),
    startCommand: nullableString(item.startCommand, `${label}.startCommand`),
    healthCheck: health
      ? {
          kind: enumValue(health.kind, healthKinds, `${label}.healthCheck.kind`),
          port: number(health.port, `${label}.healthCheck.port`),
        }
      : null,
    orderIndex: number(item.orderIndex ?? 0, `${label}.orderIndex`),
    stateless: boolean(item.stateless ?? false, `${label}.stateless`),
    bootstrap: parseBootstrap(item.bootstrap, `${label}.bootstrap`),
    endpointObservation: enumValue(item.endpointObservation ?? 0, observationModes, `${label}.endpointObservation`),
    bakeAtPublish: boolean(item.bakeAtPublish ?? false, `${label}.bakeAtPublish`),
    imageDigest: nullableString(item.imageDigest, `${label}.imageDigest`),
  }
}

function parseInfrastructure(value: unknown, label: string): TeamLabTopologyInfrastructure {
  const item = record(value, label)
  return {
    key: string(item.key, `${label}.key`),
    name: string(item.name, `${label}.name`),
    kind: enumValue(item.kind, infrastructureKinds, `${label}.kind`),
    interfaces: array(item.interfaces, `${label}.interfaces`, parseInterface),
    networkKey: nullableString(item.networkKey, `${label}.networkKey`),
  }
}

function parseConnection(value: unknown, label: string): TeamLabTopologyConnection {
  const item = record(value, label)
  return {
    key: string(item.key, `${label}.key`),
    fromNetworkKey: string(item.fromNetworkKey, `${label}.fromNetworkKey`),
    toNetworkKey: string(item.toNetworkKey, `${label}.toNetworkKey`),
    viaAssetKey: nullableString(item.viaAssetKey, `${label}.viaAssetKey`),
    viaNodeKey: nullableString(item.viaNodeKey, `${label}.viaNodeKey`),
    direction: enumValue(item.direction ?? 1, directions, `${label}.direction`),
  }
}

function parseDependency(value: unknown, label: string): TeamLabTopologyDependency {
  const item = record(value, label)
  return {
    assetKey: string(item.assetKey, `${label}.assetKey`),
    dependsOnKey: string(item.dependsOnKey, `${label}.dependsOnKey`),
    condition: enumValue(item.condition, dependencyConditions, `${label}.condition`),
  }
}

function parseObservation(value: unknown, label: string): TeamLabObservationPolicy {
  if (value === null || value === undefined) {
    return { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 'optional' }
  }
  const item = record(value, label)
  return {
    flowMetadataEnabled: boolean(item.flowMetadataEnabled ?? true, `${label}.flowMetadataEnabled`),
    onDemandPcapEnabled: boolean(item.onDemandPcapEnabled ?? true, `${label}.onDemandPcapEnabled`),
    endpointObservation: enumValue(item.endpointObservation ?? 1, observationModes, `${label}.endpointObservation`),
  }
}

export function parseTeamLabDefinition(
  value: unknown,
  label = 'TeamLab topology definition'
): TeamLabTopologyDefinition {
  const item = record(value, label)
  return {
    name: string(item.name, `${label}.name`),
    networks: array(item.networks, `${label}.networks`, parseNetwork),
    infrastructure: optionalArray(item.infrastructure, `${label}.infrastructure`, parseInfrastructure),
    assets: array(item.assets, `${label}.assets`, parseAsset),
    connections: array(item.connections, `${label}.connections`, parseConnection),
    dependencies: optionalArray(item.dependencies, `${label}.dependencies`, parseDependency),
    observation: parseObservation(item.observation, `${label}.observation`),
  }
}

function parseEditorItem(value: unknown, label: string): TeamLabEditorItem {
  const item = record(value, label)
  return {
    x: number(item.x, `${label}.x`),
    y: number(item.y, `${label}.y`),
    width: nullableNumber(item.width, `${label}.width`),
    height: nullableNumber(item.height, `${label}.height`),
    collapsed: boolean(item.collapsed ?? false, `${label}.collapsed`),
  }
}

function editorItems(value: unknown, label: string): Record<string, TeamLabEditorItem> {
  if (value === null || value === undefined) return {}
  const source = record(value, label)
  return Object.fromEntries(
    Object.entries(source).map(([key, item]) => [key, parseEditorItem(item, `${label}.${key}`)])
  )
}

export function parseTeamLabEditor(value: unknown, label = 'TeamLab editor metadata'): TeamLabTopologyEditor {
  const item = record(value, label)
  return {
    networks: editorItems(item.networks, `${label}.networks`),
    assets: editorItems(item.assets, `${label}.assets`),
    infrastructure: editorItems(item.infrastructure, `${label}.infrastructure`),
  }
}

export function parseTeamLabTopologyDetail(value: unknown): TeamLabTopologyDetail {
  const item = record(value, 'TeamLab topology detail')
  return {
    id: string(item.id, 'TeamLab topology detail.id'),
    revision: number(item.revision, 'TeamLab topology detail.revision'),
    schemaVersion: number(item.schemaVersion, 'TeamLab topology detail.schemaVersion'),
    definition: parseTeamLabDefinition(item.definition),
    editor: parseTeamLabEditor(item.editor),
    createdAt: number(item.createdAt, 'TeamLab topology detail.createdAt'),
    updatedAt: number(item.updatedAt, 'TeamLab topology detail.updatedAt'),
  }
}

function parseSummary(value: unknown, label: string): TeamLabTopologySummary {
  const item = record(value, label)
  return {
    id: string(item.id, `${label}.id`),
    name: string(item.name, `${label}.name`),
    revision: number(item.revision, `${label}.revision`),
    schemaVersion: number(item.schemaVersion, `${label}.schemaVersion`),
    createdAt: number(item.createdAt, `${label}.createdAt`),
    updatedAt: number(item.updatedAt, `${label}.updatedAt`),
  }
}

export const parseTeamLabTopologyList = (value: unknown) => array(value, 'TeamLab topology list', parseSummary)

function parseAdminReleaseSummary(value: unknown, label: string) {
  const item = record(value, label)
  return {
    id: string(item.id, `${label}.id`),
    version: number(item.version, `${label}.version`),
    sourceRevision: number(item.sourceRevision, `${label}.sourceRevision`),
    contentHash: string(item.contentHash, `${label}.contentHash`),
    publishedAt: number(item.publishedAt, `${label}.publishedAt`),
  }
}

function parseAdminRuntimeSummary(value: unknown, label: string): TeamLabAdminRuntimeSummary {
  const item = record(value, label)
  return {
    id: string(item.id, `${label}.id`),
    releaseId: string(item.releaseId, `${label}.releaseId`),
    status: enumValue(item.status, runtimeStatuses, `${label}.status`),
    stage: string(item.stage, `${label}.stage`),
    openForAccess: boolean(item.openForAccess, `${label}.openForAccess`),
    createdAt: number(item.createdAt, `${label}.createdAt`),
    updatedAt: nullableNumber(item.updatedAt, `${label}.updatedAt`),
    error: nullableString(item.error, `${label}.error`),
  }
}

export function parseTeamLabAdminScenePage(value: unknown): TeamLabAdminScenePage {
  const page = record(value, 'TeamLab scene page')
  return {
    items: array(page.items, 'TeamLab scene page.items', (entry, label) => {
      const item = record(entry, label)
      const validation =
        item.validation === null || item.validation === undefined
          ? null
          : record(item.validation, `${label}.validation`)
      return {
        id: string(item.id, `${label}.id`),
        name: string(item.name, `${label}.name`),
        ownerId: nullableString(item.ownerId, `${label}.ownerId`),
        ownerDisplayName: string(item.ownerDisplayName, `${label}.ownerDisplayName`),
        revision: number(item.revision, `${label}.revision`),
        schemaVersion: number(item.schemaVersion, `${label}.schemaVersion`),
        networkCount: number(item.networkCount, `${label}.networkCount`),
        assetCount: number(item.assetCount, `${label}.assetCount`),
        infrastructureCount: number(item.infrastructureCount, `${label}.infrastructureCount`),
        latestRelease:
          item.latestRelease === null || item.latestRelease === undefined
            ? null
            : parseAdminReleaseSummary(item.latestRelease, `${label}.latestRelease`),
        validation: validation
          ? {
              revision: number(validation.revision, `${label}.validation.revision`),
              valid: boolean(validation.valid, `${label}.validation.valid`),
              issueCount: number(validation.issueCount, `${label}.validation.issueCount`),
              validatedAt: number(validation.validatedAt, `${label}.validation.validatedAt`),
            }
          : null,
        latestTrialRuntime:
          item.latestTrialRuntime === null || item.latestTrialRuntime === undefined
            ? null
            : parseAdminRuntimeSummary(item.latestTrialRuntime, `${label}.latestTrialRuntime`),
        gameReferenceCount: number(item.gameReferenceCount, `${label}.gameReferenceCount`),
        createdAt: number(item.createdAt, `${label}.createdAt`),
        updatedAt: number(item.updatedAt, `${label}.updatedAt`),
      }
    }),
    nextCursor: nullableString(page.nextCursor, 'TeamLab scene page.nextCursor'),
  }
}

export function parseTeamLabAdminRuntimePage(value: unknown): TeamLabAdminRuntimePage {
  const page = record(value, 'TeamLab runtime page')
  return {
    items: array(page.items, 'TeamLab runtime page.items', parseAdminRuntimeSummary),
    nextCursor: nullableString(page.nextCursor, 'TeamLab runtime page.nextCursor'),
  }
}

export function parseTeamLabValidation(value: unknown): TeamLabValidationResult {
  const item = record(value, 'TeamLab validation')
  return {
    valid: boolean(item.valid, 'TeamLab validation.valid'),
    issues: array(item.issues, 'TeamLab validation.issues', (issue, label) => {
      const entry = record(issue, label)
      return {
        code: string(entry.code, `${label}.code`),
        path: string(entry.path, `${label}.path`),
        message: string(entry.message, `${label}.message`),
      }
    }),
  }
}

function parseRelease(value: unknown, label: string): TeamLabRelease {
  const item = record(value, label)
  return {
    id: string(item.id, `${label}.id`),
    topologyId: string(item.topologyId, `${label}.topologyId`),
    version: number(item.version, `${label}.version`),
    sourceRevision: number(item.sourceRevision, `${label}.sourceRevision`),
    schemaVersion: number(item.schemaVersion, `${label}.schemaVersion`),
    contentHash: string(item.contentHash, `${label}.contentHash`),
    publishedBy: nullableString(item.publishedBy, `${label}.publishedBy`),
    publishedAt: number(item.publishedAt, `${label}.publishedAt`),
  }
}

export const parseTeamLabRelease = (value: unknown) => parseRelease(value, 'TeamLab release')
export const parseTeamLabReleaseList = (value: unknown) => array(value, 'TeamLab release list', parseRelease)

export function parseTeamLabCapabilities(value: unknown): TeamLabCapabilities {
  const item = record(value, 'TeamLab capabilities')
  const features = record(item.features, 'TeamLab capabilities.features')
  const limits = record(item.limits, 'TeamLab capabilities.limits')
  return {
    apiVersion: string(item.apiVersion, 'TeamLab capabilities.apiVersion'),
    topologySchemaVersions: array(item.topologySchemaVersions, 'TeamLab capabilities.topologySchemaVersions', number),
    assetKinds: array(item.assetKinds, 'TeamLab capabilities.assetKinds', (entry, label) =>
      enumValue(entry, assetKinds, label)
    ),
    networkModel: string(item.networkModel, 'TeamLab capabilities.networkModel'),
    features: {
      multiNode: boolean(features.multiNode, 'TeamLab capabilities.features.multiNode'),
      linuxVm: boolean(features.linuxVm, 'TeamLab capabilities.features.linuxVm'),
      windowsVm: boolean(features.windowsVm, 'TeamLab capabilities.features.windowsVm'),
      trafficFlows: boolean(features.trafficFlows, 'TeamLab capabilities.features.trafficFlows'),
      onDemandPcap: boolean(features.onDemandPcap, 'TeamLab capabilities.features.onDemandPcap'),
    },
    limits: {
      networksPerTopology: number(limits.networksPerTopology, 'TeamLab capabilities.limits.networksPerTopology'),
      assetsPerTopology: number(limits.assetsPerTopology, 'TeamLab capabilities.limits.assetsPerTopology'),
      interfacesPerAsset: number(limits.interfacesPerAsset, 'TeamLab capabilities.limits.interfacesPerAsset'),
    },
  }
}

export function parseTeamLabPlan(value: unknown): TeamLabPlan {
  const item = record(value, 'TeamLab plan')
  return {
    topologyId: string(item.topologyId, 'TeamLab plan.topologyId'),
    releaseId: string(item.releaseId, 'TeamLab plan.releaseId'),
    networks: array(item.networks, 'TeamLab plan.networks', (entry, label) => {
      const network = record(entry, label)
      return {
        key: string(network.key, `${label}.key`),
        name: string(network.name, `${label}.name`),
        candidateCidr: string(network.candidateCidr, `${label}.candidateCidr`),
        isEntry: boolean(network.isEntry, `${label}.isEntry`),
      }
    }),
    assets: array(item.assets, 'TeamLab plan.assets', (entry, label) => {
      const asset = record(entry, label)
      const resources = record(asset.resources, `${label}.resources`)
      return {
        key: string(asset.key, `${label}.key`),
        name: string(asset.name, `${label}.name`),
        kind: enumValue(asset.kind, assetKinds, `${label}.kind`),
        imageTemplateId: number(asset.imageTemplateId, `${label}.imageTemplateId`),
        resources: {
          cpuUnits: number(resources.cpuUnits, `${label}.resources.cpuUnits`),
          memoryMiB: number(resources.memoryMiB, `${label}.resources.memoryMiB`),
          storageMiB: number(resources.storageMiB, `${label}.resources.storageMiB`),
        },
        interfaces: array(asset.interfaces, `${label}.interfaces`, (iface, ifaceLabel) => {
          const parsed = parseInterface(iface, ifaceLabel)
          const { orderIndex: _, ...result } = parsed
          return result
        }),
        routingEnabled: boolean(asset.routingEnabled, `${label}.routingEnabled`),
        imageDigest: nullableString(asset.imageDigest, `${label}.imageDigest`),
      }
    }),
    shards: array(item.shards, 'TeamLab plan.shards', (entry, label) => {
      const shard = record(entry, label)
      return {
        key: string(shard.key, `${label}.key`),
        networkKeys: array(shard.networkKeys, `${label}.networkKeys`, string),
        assetKeys: array(shard.assetKeys, `${label}.assetKeys`, string),
        dockerSlots: number(shard.dockerSlots, `${label}.dockerSlots`),
        vmSlots: number(shard.vmSlots, `${label}.vmSlots`),
        infrastructureKeys: optionalArray(shard.infrastructureKeys, `${label}.infrastructureKeys`, string),
      }
    }),
    crossShardConnections: number(item.crossShardConnections, 'TeamLab plan.crossShardConnections'),
    requiredCapabilities: array(item.requiredCapabilities, 'TeamLab plan.requiredCapabilities', string),
    warnings: array(item.warnings, 'TeamLab plan.warnings', string),
    planHash: string(item.planHash, 'TeamLab plan.planHash'),
    managedInfrastructureCount: number(item.managedInfrastructureCount ?? 0, 'TeamLab plan.managedInfrastructureCount'),
    bootstrapArtifactCount: number(item.bootstrapArtifactCount ?? 0, 'TeamLab plan.bootstrapArtifactCount'),
    observationPointEstimate: number(item.observationPointEstimate ?? 0, 'TeamLab plan.observationPointEstimate'),
  }
}

export function parseTeamLabReleaseReadiness(value: unknown): TeamLabAdminReleaseReadiness {
  const item = record(value, 'TeamLab release readiness')
  return {
    topologyId: string(item.topologyId, 'TeamLab release readiness.topologyId'),
    releaseId: string(item.releaseId, 'TeamLab release readiness.releaseId'),
    ready: boolean(item.ready, 'TeamLab release readiness.ready'),
    plan: item.plan === null || item.plan === undefined ? null : parseTeamLabPlan(item.plan),
    images: array(item.images, 'TeamLab release readiness.images', (entry, label) => {
      const image = record(entry, label)
      return {
        imageTemplateId: number(image.imageTemplateId, `${label}.imageTemplateId`),
        name: string(image.name, `${label}.name`),
        imageType: enumValue(image.imageType, imageTypes, `${label}.imageType`),
        digest: string(image.digest, `${label}.digest`),
        eligibleNodeCount: number(image.eligibleNodeCount, `${label}.eligibleNodeCount`),
        readyNodeCount: number(image.readyNodeCount, `${label}.readyNodeCount`),
        pendingNodeCount: number(image.pendingNodeCount, `${label}.pendingNodeCount`),
        failedNodeCount: number(image.failedNodeCount, `${label}.failedNodeCount`),
      }
    }),
    latestTrialRuntime:
      item.latestTrialRuntime === null || item.latestTrialRuntime === undefined
        ? null
        : parseAdminRuntimeSummary(item.latestTrialRuntime, 'TeamLab release readiness.latestTrialRuntime'),
    blockingReasons: array(item.blockingReasons, 'TeamLab release readiness.blockingReasons', string),
  }
}

const assetKindWire: Record<TeamLabAssetKind, number> = { docker: 0, vm: 1 }
const infrastructureKindWire: Record<TeamLabInfrastructureKind, number> = { 'managed-switch': 0, 'managed-router': 1 }
const directionWire: Record<TeamLabConnectionDirection, number> = { 'from-to': 0, bidirectional: 1 }
const dependencyWire: Record<TeamLabDependencyCondition, number> = {
  'network-ready': 0,
  'guest-ready': 1,
  'service-ready': 2,
  'bootstrap-completed': 3,
}
const observationWire: Record<TeamLabEndpointObservationMode, number> = { disabled: 0, optional: 1, required: 2 }
const healthWire: Record<TeamLabHealthCheckKind, number> = { tcp: 0, http: 1 }

export function serializeTeamLabWriteRequest(request: CreateTeamLabTopologyRequest | UpdateTeamLabTopologyRequest) {
  return {
    ...request,
    infrastructure: request.infrastructure.map((item) => ({ ...item, kind: infrastructureKindWire[item.kind] })),
    assets: request.assets.map((item) => ({
      ...item,
      kind: assetKindWire[item.kind],
      healthCheck: item.healthCheck ? { ...item.healthCheck, kind: healthWire[item.healthCheck.kind] } : null,
      endpointObservation: observationWire[item.endpointObservation],
    })),
    connections: request.connections.map((item) => ({ ...item, direction: directionWire[item.direction] })),
    dependencies: request.dependencies.map((item) => ({ ...item, condition: dependencyWire[item.condition] })),
    observation: {
      ...request.observation,
      endpointObservation: observationWire[request.observation.endpointObservation],
    },
  }
}
