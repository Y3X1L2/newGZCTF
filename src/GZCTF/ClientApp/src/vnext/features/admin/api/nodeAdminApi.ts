import type { EnableTeamLabNetworkRequest, NodeDeployRequest, UpdateNodeRequest } from '@Api'
import {
  contractFailure,
  isBoolean,
  isNullableNumber,
  isNullableString,
  isNumber,
  isRecord,
  isString,
  isStringArray,
} from './contractParsers'
import type { NodeDeployResult, NodeResourceItem, NodeResourcePage, NodeSummary, NodeUpdateResult } from './contracts'
import { runtimeJsonClient, type RuntimeJsonClient } from './runtimeJsonClient'

export interface NodeResourceQuery {
  type?: string
  status?: string
  page?: number
  pageSize?: number
}

function isCapabilityAvailability(value: unknown) {
  return (
    isRecord(value) &&
    isNullableString(value.docker) &&
    isNullableString(value.kvm) &&
    isNullableString(value.teamLabNetwork) &&
    isNullableString(value.teamLabDocker) &&
    isNullableString(value.teamLabVm)
  )
}

function isAgentExecutionLimits(value: unknown) {
  return (
    value === null ||
    (isRecord(value) &&
      isNumber(value.dockerCreates) &&
      isNumber(value.vmCreates) &&
      isNumber(value.dockerImageTransfers) &&
      isNumber(value.vmImageTransfers) &&
      isNumber(value.teamLabNetworkOperations) &&
      isNumber(value.controlOperations))
  )
}

function isNodeSummary(value: unknown): value is NodeSummary {
  return (
    isRecord(value) &&
    isString(value.id) &&
    isString(value.name) &&
    isString(value.hostAddress) &&
    isNumber(value.status) &&
    isNumber(value.capabilities) &&
    isNumber(value.cpuLoad) &&
    isNumber(value.memoryLoad) &&
    isNumber(value.currentContainers) &&
    isNumber(value.maxContainers) &&
    isNumber(value.reservedContainers) &&
    isNumber(value.allocatedContainers) &&
    isNumber(value.currentVms) &&
    isNumber(value.maxVms) &&
    isNumber(value.reservedVms) &&
    isNumber(value.allocatedVms) &&
    isNumber(value.usedPorts) &&
    isNumber(value.totalPorts) &&
    isNumber(value.portPoolStart) &&
    isNumber(value.portPoolEnd) &&
    isString(value.portPoolMode) &&
    isNullableNumber(value.lastHeartbeat) &&
    isBoolean(value.isSchedulable) &&
    isBoolean(value.isLocal) &&
    isNumber(value.agentPort) &&
    isBoolean(value.teamLabNetworkEnabled) &&
    isNumber(value.teamLabTunnelStatus) &&
    isNullableString(value.teamLabTunnelIp) &&
    isNullableNumber(value.teamLabTunnelLastHandshake) &&
    isNullableString(value.teamLabTunnelLastError) &&
    isNumber(value.teamLabTunnelConfigVersion) &&
    isNullableString(value.agentVersion) &&
    isNullableString(value.agentBinarySha256) &&
    isNumber(value.capabilityManifestSchemaVersion) &&
    isNullableString(value.capabilityHash) &&
    isNullableNumber(value.capabilityObservedAt) &&
    isStringArray(value.agentFeatures) &&
    isAgentExecutionLimits(value.agentExecutionLimits) &&
    isNullableString(value.teamLabFabricIp) &&
    isNumber(value.teamLabFabricStatus) &&
    isBoolean(value.canHostTeamLab) &&
    isBoolean(value.canHostTeamLabFabric) &&
    isBoolean(value.canHostTeamLabDocker) &&
    isBoolean(value.canHostTeamLabVm) &&
    isStringArray(value.unschedulableReasons) &&
    isCapabilityAvailability(value.unschedulableByCapability) &&
    isStringArray(value.schedulableCapabilities)
  )
}

function isNodeResource(value: unknown): value is NodeResourceItem {
  return (
    isRecord(value) &&
    isString(value.kind) &&
    isString(value.id) &&
    isString(value.name) &&
    isString(value.status) &&
    isBoolean(value.isActive) &&
    isNumber(value.startedAt) &&
    isNullableNumber(value.expectedStopAt) &&
    isNullableNumber(value.stoppedAt) &&
    isString(value.duration) &&
    isNullableString(value.image) &&
    isNullableString(value.runtimeId) &&
    isNullableString(value.entry) &&
    isNullableString(value.ip) &&
    isNullableNumber(value.port) &&
    isNullableNumber(value.gameId) &&
    isNullableString(value.gameTitle) &&
    isNullableNumber(value.challengeId) &&
    isNullableString(value.challengeTitle) &&
    isNullableString(value.challengeCategory) &&
    isNullableNumber(value.teamId) &&
    isNullableString(value.teamName) &&
    isNullableString(value.userId) &&
    isNullableString(value.userName) &&
    isNullableString(value.providerName) &&
    isNullableString(value.osType)
  )
}

function parseNode(value: unknown, label: string) {
  if (!isNodeSummary(value)) return contractFailure(label, value)
  return value
}

function parseResources(value: unknown): NodeResourcePage {
  if (
    !isRecord(value) ||
    !isString(value.nodeId) ||
    !isString(value.nodeName) ||
    !isNumber(value.page) ||
    !isNumber(value.pageSize) ||
    !isNumber(value.total) ||
    !isNumber(value.runningCount) ||
    !isNumber(value.containerCount) ||
    !isNumber(value.vmCount) ||
    !isNumber(value.pentestCount) ||
    !isNumber(value.teamLabCount) ||
    !Array.isArray(value.items) ||
    !value.items.every(isNodeResource)
  ) {
    return contractFailure('Node resource list', value)
  }
  return value as unknown as NodeResourcePage
}

function parseDeployResult(value: unknown): NodeDeployResult {
  if (
    !isRecord(value) ||
    !isBoolean(value.success) ||
    !isString(value.nodeId) ||
    !isNullableString(value.nodeName) ||
    !isNumber(value.capabilities) ||
    !isString(value.message)
  ) {
    return contractFailure('Node registration', value)
  }
  return value as unknown as NodeDeployResult
}

function parseUpdateResult(value: unknown): NodeUpdateResult {
  if (
    !isRecord(value) ||
    !isString(value.id) ||
    !isBoolean(value.isSchedulable) ||
    !isBoolean(value.isLocal) ||
    !isNumber(value.maxContainers) ||
    !isNumber(value.maxVms)
  ) {
    return contractFailure('Node update', value)
  }
  return value as unknown as NodeUpdateResult
}

export function createNodeAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async list() {
      const value = await client.get('/api/v1/nodes')
      if (!Array.isArray(value) || !value.every(isNodeSummary)) return contractFailure('Node list', value)
      return value
    },

    async detail(id: string) {
      return parseNode(await client.get(`/api/v1/nodes/${id}`), 'Node detail')
    },

    async resources(id: string, query: NodeResourceQuery = {}) {
      return parseResources(
        await client.get(`/api/v1/nodes/${id}/resources`, {
          type: query.type ?? 'all',
          status: query.status ?? 'all',
          page: query.page ?? 1,
          pageSize: query.pageSize ?? 12,
        })
      )
    },

    async register(data: NodeDeployRequest) {
      return parseDeployResult(await client.postJson('/api/v1/nodes', data))
    },

    async update(id: string, data: UpdateNodeRequest) {
      return parseUpdateResult(await client.patchJson(`/api/v1/nodes/${id}`, data))
    },

    async enableTeamLab(id: string, data: EnableTeamLabNetworkRequest) {
      const value = await client.postJson(`/api/v1/nodes/${id}/teamlab/enable`, data)
      if (!isRecord(value)) return contractFailure('TeamLab network action', value)
      return value
    },

    async syncAgent(id: string) {
      const value = await client.postJson(`/api/v1/nodes/${id}/sync-agent`)
      if (!isRecord(value)) return contractFailure('Agent synchronization', value)
      return value
    },

    async deregister(id: string, force = false) {
      await client.delete(`/api/v1/nodes/${id}`, { force })
    },

    async destroyVm(instanceId: string) {
      await client.delete(`/api/v1/nodes/vms/${instanceId}/admin`)
    },
  }
}

export const nodeAdminApi = createNodeAdminApi()
