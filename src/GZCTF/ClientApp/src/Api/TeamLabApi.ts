import api, { ContentType, RequestParams, RequestResponse } from '@Api'

export enum TeamLabAssetKind {
  Docker = 'Docker',
  Vm = 'Vm',
}

export enum TeamLabRuntimeStatus {
  Pending = 'Pending',
  Planning = 'Planning',
  Scheduled = 'Scheduled',
  Deploying = 'Deploying',
  Probing = 'Probing',
  Running = 'Running',
  Failed = 'Failed',
  CleanupPending = 'CleanupPending',
  Stopped = 'Stopped',
  Destroying = 'Destroying',
  Destroyed = 'Destroyed',
}

export enum TeamLabTrafficCaptureStatus {
  Pending = 'Pending',
  Running = 'Running',
  Stopping = 'Stopping',
  Completed = 'Completed',
  Failed = 'Failed',
  Expired = 'Expired',
}

export interface TeamLabAddressPoolModel {
  poolCidr: string
  runtimePrefixLength: number
}

export interface TeamLabTopologyNetworkModel {
  key: string
  name: string
  addressPool: TeamLabAddressPoolModel
  isEntry: boolean
  orderIndex: number
}

export interface TeamLabTopologyInterfaceModel {
  key: string
  networkKey: string
  hostOffset: number
  primary: boolean
  orderIndex: number
}

export interface TeamLabTopologyAssetModel {
  key: string
  name: string
  kind: TeamLabAssetKind
  imageTemplateId: number
  resources: { cpuUnits: number; memoryMiB: number; storageMiB: number }
  interfaces: TeamLabTopologyInterfaceModel[]
  routingEnabled: boolean
  exposePort?: number | null
  environment?: Record<string, string> | null
  startCommand?: string | null
  healthCheck?: { kind: 'Tcp' | 'Http'; port: number } | null
  orderIndex: number
}

export interface TeamLabTopologyConnectionModel {
  key: string
  fromNetworkKey: string
  toNetworkKey: string
  viaAssetKey: string
}

export interface TeamLabEditorItemModel {
  x: number
  y: number
  width?: number | null
  height?: number | null
  collapsed?: boolean
}

export interface TeamLabTopologyEditorModel {
  networks: Record<string, TeamLabEditorItemModel>
  assets: Record<string, TeamLabEditorItemModel>
}

export interface TeamLabTopologyDefinitionModel {
  name: string
  networks: TeamLabTopologyNetworkModel[]
  assets: TeamLabTopologyAssetModel[]
  connections: TeamLabTopologyConnectionModel[]
}

export interface TeamLabTopologyDetailModel {
  id: string
  revision: number
  schemaVersion: number
  definition: TeamLabTopologyDefinitionModel
  editor: TeamLabTopologyEditorModel
  createdAt: string
  updatedAt: string
}

export interface TeamLabTopologySummaryModel {
  id: string
  name: string
  revision: number
  schemaVersion: number
  createdAt: string
  updatedAt: string
}

export interface TeamLabValidationResultModel {
  valid: boolean
  issues: Array<{ code: string; path: string; message: string }>
}

export interface TeamLabReleaseModel {
  id: string
  topologyId: string
  version: number
  sourceRevision: number
  schemaVersion: number
  contentHash: string
  publishedAt: string
}

export interface TeamLabPlanModel {
  topologyId: string
  releaseId: string
  networks: Array<{ key: string; name: string; candidateCidr: string; isEntry: boolean }>
  assets: Array<{ key: string; name: string; kind: TeamLabAssetKind; imageTemplateId: number }>
  shards: Array<{ key: string; networkKeys: string[]; assetKeys: string[]; dockerSlots: number; vmSlots: number }>
  crossShardConnections: number
  requiredCapabilities: string[]
  warnings: string[]
  planHash: string
}

export interface TeamLabRuntimeProjectionModel {
  id: string
  releaseId: string
  generation: number
  status: TeamLabRuntimeStatus
  stage: string
  openForAccess: boolean
  shards: Array<{ id: string; status: TeamLabRuntimeStatus; networkKeys: string[]; assetKeys: string[]; error?: string | null }>
  networks: Array<{ key: string; name: string; cidr: string; gatewayIp: string }>
  assets: Array<{
    key: string
    name: string
    kind: TeamLabAssetKind
    runtimeResourceId?: string | null
    primaryIp?: string | null
    status: TeamLabRuntimeStatus
    error?: string | null
  }>
  createdAt: string
  updatedAt?: string | null
  error?: string | null
}

export interface TeamLabTrafficFlowPageModel {
  items: Array<{
    cursor: string
    shardId: string
    networkKey: string
    sourceIp: string
    sourcePort?: number | null
    destinationIp: string
    destinationPort?: number | null
    protocol: string
    bytes: number
    packets: number
    firstSeen: string
    lastSeen: string
  }>
  nextCursor?: string | null
}

export interface TeamLabCaptureModel {
  id: string
  status: TeamLabTrafficCaptureStatus
  scope: string
  networkKey?: string | null
  maxBytes: number
  maxSeconds: number
  capturedBytes: number
  createdAt: string
  startedAt?: string | null
  completedAt?: string | null
  expiresAt?: string | null
  error?: string | null
}

export interface ImageTemplateLite {
  id: number
  name: string
  imageType?: string | number
  osType?: string | number
  status?: string | number
}

const request = api.request
const json = ContentType.Json

export const teamLabAdminApi = {
  listTopologies: (params: RequestParams = {}) =>
    request<TeamLabTopologySummaryModel[], RequestResponse>({ path: '/api/admin/teamlab/topologies', method: 'GET', format: 'json', ...params }),
  createTopology: (data: TeamLabTopologyDefinitionModel & { editor?: TeamLabTopologyEditorModel }, params: RequestParams = {}) =>
    request<TeamLabTopologyDetailModel, RequestResponse>({ path: '/api/admin/teamlab/topologies', method: 'POST', body: data, type: json, format: 'json', ...params }),
  getTopology: (id: string, params: RequestParams = {}) =>
    request<TeamLabTopologyDetailModel, RequestResponse>({ path: `/api/admin/teamlab/topologies/${id}`, method: 'GET', format: 'json', ...params }),
  updateTopology: (id: string, data: TeamLabTopologyDefinitionModel & { revision: number; editor: TeamLabTopologyEditorModel }, params: RequestParams = {}) =>
    request<TeamLabTopologyDetailModel, RequestResponse>({ path: `/api/admin/teamlab/topologies/${id}`, method: 'PUT', body: data, type: json, format: 'json', ...params }),
  validateTopology: (id: string, params: RequestParams = {}) =>
    request<TeamLabValidationResultModel, RequestResponse>({ path: `/api/admin/teamlab/topologies/${id}/validate`, method: 'POST', format: 'json', ...params }),
  publishTopology: (id: string, revision: number, params: RequestParams = {}) =>
    request<TeamLabReleaseModel, RequestResponse>({ path: `/api/admin/teamlab/topologies/${id}/releases`, method: 'POST', body: { revision }, type: json, format: 'json', ...params }),
  listReleases: (id: string, params: RequestParams = {}) =>
    request<TeamLabReleaseModel[], RequestResponse>({ path: `/api/admin/teamlab/topologies/${id}/releases`, method: 'GET', format: 'json', ...params }),
  plan: (topologyId: string, releaseId: string, params: RequestParams = {}) =>
    request<TeamLabPlanModel, RequestResponse>({ path: `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/plan`, method: 'POST', format: 'json', ...params }),
  getRuntime: (runtimeId: string, params: RequestParams = {}) =>
    request<TeamLabRuntimeProjectionModel, RequestResponse>({ path: `/api/admin/teamlab/runtimes/${runtimeId}`, method: 'GET', format: 'json', ...params }),
  getFlows: (runtimeId: string, limit = 100, params: RequestParams = {}) =>
    request<TeamLabTrafficFlowPageModel, RequestResponse>({ path: `/api/admin/teamlab/runtimes/${runtimeId}/traffic/flows`, method: 'GET', query: { limit }, format: 'json', ...params }),
  startCapture: (runtimeId: string, data: { scope: string; networkKey?: string; maxSeconds: number; maxBytes: number; expiresInSeconds: number }, params: RequestParams = {}) =>
    request<TeamLabCaptureModel, RequestResponse>({ path: `/api/admin/teamlab/runtimes/${runtimeId}/captures`, method: 'POST', body: data, type: json, format: 'json', ...params }),
  getCapture: (runtimeId: string, captureId: string, params: RequestParams = {}) =>
    request<TeamLabCaptureModel, RequestResponse>({ path: `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}`, method: 'GET', format: 'json', ...params }),
  stopCapture: (runtimeId: string, captureId: string, params: RequestParams = {}) =>
    request<TeamLabCaptureModel, RequestResponse>({ path: `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/stop`, method: 'POST', format: 'json', ...params }),
  captureDownloadUrl: (runtimeId: string, captureId: string) => `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/download`,
}
