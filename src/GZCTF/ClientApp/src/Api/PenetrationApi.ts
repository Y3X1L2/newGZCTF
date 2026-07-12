import api, { AnswerResult, ContentType, RequestParams, RequestResponse } from '@Api'
import { TeamLabRuntimeStatus } from './TeamLabApi'

export interface PenetrationObjectiveWriteModel {
  key: string
  assetKey: string
  title: string
  description?: string | null
  category: string
  score: number
  dynamic: boolean
  staticFlag?: string | null
  flagTemplate?: string | null
  maxAttempts: number
  visible: boolean
  checkpoint: boolean
  prerequisiteKeys: string[]
  orderIndex: number
}
export interface PenetrationObjectiveModel extends Omit<PenetrationObjectiveWriteModel, 'staticFlag' | 'flagTemplate'> {
  id: number
}

export interface PenetrationGameLabBindingModel {
  gameId: number
  topologyId: string
  activeReleaseId?: string | null
  maxResetCount: number
  objectives: PenetrationObjectiveModel[]
}

export interface PenetrationWorkspaceObjectiveModel {
  id: number
  key: string
  assetKey: string
  title: string
  description?: string | null
  category: string
  score: number
  solved: boolean
  attempts: number
  maxAttempts: number
  checkpoint: boolean
  prerequisiteKeys: string[]
}

export interface PenetrationWorkspaceModel {
  gameId: number
  teamId: number
  teamName: string
  runtimeId: string
  status: TeamLabRuntimeStatus
  stage: string
  resetCount: number
  maxResetCount: number
  objectives: PenetrationWorkspaceObjectiveModel[]
}

export interface PenetrationSubmitResultModel {
  accepted: boolean
  score: number
  message: string
}

export interface PenetrationScoreboardItemModel {
  rank: number
  teamId: number
  teamName: string
  score: number
  solvedCount: number
  lastSubmissionTime: string
}

export interface PenetrationSubmissionLogModel {
  id: number
  time: string
  teamId: number
  teamName: string
  userName: string
  assetKey: string
  objectiveTitle: string
  category: string
  score: number
  status: AnswerResult
}

export interface PenetrationSubmissionPageModel {
  items: PenetrationSubmissionLogModel[]
  total: number
}

export interface PenetrationRuntimeBindingModel {
  teamId: number
  teamName: string
  runtimeId: string
  generation: number
  status: TeamLabRuntimeStatus
  stage: string
  shardCount: number
  assetCount: number
  createdAt: string
  updatedAt?: string | null
  error?: string | null
}

export interface PenetrationWorkspaceUpdateModel {
  gameId: number
  teamId: number
  runtimeId: string
  time: string
}

export interface TeamLabAccessGrantModel {
  id: string
  type: string
  clientAddress: string
  endpoint: string
  allowedIps: string
  dns: string
  createdAt: string
  expiresAt?: string | null
  configurationDownloadUrl?: string | null
}

const request = api.request
const json = ContentType.Json

export const penetrationAdminApi = {
  getBinding: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationGameLabBindingModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/binding`, method: 'GET', format: 'json', ...params }),
  bindTopology: (gameId: number, topologyId: string, params: RequestParams = {}) =>
    request<PenetrationGameLabBindingModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/binding`, method: 'PUT', body: { topologyId }, type: json, format: 'json', ...params }),
  replaceObjectives: (gameId: number, maxResetCount: number, objectives: PenetrationObjectiveWriteModel[], params: RequestParams = {}) =>
    request<PenetrationObjectiveModel[], RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/objectives`, method: 'PUT', body: { maxResetCount, objectives }, type: json, format: 'json', ...params }),
  activateRelease: (gameId: number, releaseId: string, params: RequestParams = {}) =>
    request<PenetrationGameLabBindingModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/releases/${releaseId}/activate`, method: 'POST', format: 'json', ...params }),
  deploy: (gameId: number, params: RequestParams = {}) =>
    request<{ message: string }, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/deploy`, method: 'POST', format: 'json', ...params }),
  stop: (gameId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/stop`, method: 'POST', format: 'json', ...params }),
  rebuildTeam: (gameId: number, teamId: number, params: RequestParams = {}) =>
    request<{ runtimeId: string }, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/rebuild`, method: 'POST', format: 'json', ...params }),
  cleanupTeam: (gameId: number, teamId: number, params: RequestParams = {}) =>
    request<RequestResponse, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/cleanup`, method: 'POST', format: 'json', ...params }),
  getRuntimes: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationRuntimeBindingModel[], RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/runtimes`, method: 'GET', format: 'json', ...params }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationScoreboardItemModel[], RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/scoreboard`, method: 'GET', format: 'json', ...params }),
  getSubmissions: (gameId: number, count = 50, skip = 0, params: RequestParams = {}) =>
    request<PenetrationSubmissionPageModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/submissions`, method: 'GET', query: { count, skip }, format: 'json', ...params }),
}

export const penetrationPlayerApi = {
  getWorkspace: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationWorkspaceModel, RequestResponse>({ path: `/api/pentest/games/${gameId}/workspace`, method: 'GET', format: 'json', ...params }),
  createAccessGrant: (gameId: number, params: RequestParams = {}) =>
    request<TeamLabAccessGrantModel, RequestResponse>({ path: `/api/pentest/games/${gameId}/access-grants`, method: 'POST', format: 'json', ...params }),
  submit: (gameId: number, objectiveId: number, flag: string, params: RequestParams = {}) =>
    request<PenetrationSubmitResultModel, RequestResponse>({ path: `/api/pentest/games/${gameId}/submit`, method: 'POST', body: { objectiveId, flag }, type: json, format: 'json', ...params }),
  reset: (gameId: number, params: RequestParams = {}) =>
    request<{ runtimeId: string }, RequestResponse>({ path: `/api/pentest/games/${gameId}/reset`, method: 'POST', format: 'json', ...params }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationScoreboardItemModel[], RequestResponse>({ path: `/api/pentest/games/${gameId}/scoreboard`, method: 'GET', format: 'json', ...params }),
}
