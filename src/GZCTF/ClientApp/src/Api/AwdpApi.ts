import api, {
  AwdpChallengeStatus,
  AwdpPatchStatus,
  AwdpRoundStatus,
  CheckerStatus,
  ContentType,
  RequestParams,
  RequestResponse,
} from '@Api'

export interface AwdpArrayResponse<T> {
  data: T[]
  length: number
  total: number
}

export interface AwdpServiceCreateModel {
  name: string
  imageName: string
  exposePort: number
  checkerScript?: string | null
  checkerEntrypoint?: string | null
  expScript?: string | null
  expEntrypoint?: string | null
  originalScore: number
  attackPoints: number
  slaPoints: number
  patchPoints: number
  serviceAbnormalPenalty: number
  maxAttackPerRound: number
  attackPhaseMinutes: number
  patchPhaseMinutes: number
  totalRounds: number
  maxResetCount: number
  maxRecoveryCount: number
}

export type AwdpServiceUpdateModel = AwdpServiceCreateModel

export interface AwdpServiceViewModel extends AwdpServiceCreateModel {
  id: number
}

export interface AwdpGameStatusModel {
  gameId: number
  currentRound: number
  roundStartTime: number
  attackPhaseMinutes: number
  patchPhaseMinutes: number
  status: AwdpRoundStatus
}

export interface AwdpTeamServiceStatus {
  instanceId: number
  serviceId: number
  serviceName: string
  teamId: number
  teamName: string
  ipAddress?: string | null
  port?: number | null
  lastCheckerStatus?: CheckerStatus | null
  isRunning: boolean
  remainingResetCount: number
  remainingRecoveryCount: number
  canManage: boolean
}

export interface AwdpServiceStatusModel {
  serviceId: number
  serviceName: string
  teamStatuses: AwdpTeamServiceStatus[]
}

export interface AwdpSubmitResultModel {
  accepted: boolean
  points: number
  roundNumber: number
  serviceId: number
  serviceName: string
  message: string
}

export interface AwdpInstanceActionModel {
  instanceId: number
  success: boolean
  message: string
}

export interface AwdpPatchSubmissionViewModel {
  id: number
  roundId: number
  roundNumber: number
  serviceId: number
  serviceName: string
  teamId: number
  teamName: string
  patchFileHash: string
  submittedAt: number
  checkerResult: CheckerStatus
  expResult: AwdpPatchStatus
  finalStatus: AwdpPatchStatus
  message?: string | null
}

export interface AwdpScoreboardItem {
  rank: number
  teamId: number
  teamName: string
  ctfScore: number
  awdpScore: number
  totalScore: number
  attackScore: number
  slaScore: number
  patchScore: number
  penaltyScore: number
}

export interface AwdpAttackLogItem {
  time: number
  attackerTeam: string
  victimTeam: string
  serviceName: string
  points: number
}

export interface AwdpPatchStatusItem {
  serviceId: number
  serviceName: string
  attackStatus: AwdpChallengeStatus
  defenseStatus: AwdpChallengeStatus
  lastPatchResult?: AwdpPatchStatus | null
  lastPatchTime?: number | null
  message?: string | null
}

const json = ContentType.Json
const form = ContentType.FormData

export const awdpAdminApi = {
  getServices: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpServiceViewModel[], RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/services`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpScoreboardItem[], RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/scoreboard`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getAttackLogs: (gameId: number, count = 100, skip = 0, params: RequestParams = {}) =>
    api.request<AwdpArrayResponse<AwdpAttackLogItem>, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/attacklogs`,
      method: 'GET',
      query: { count, skip },
      format: 'json',
      ...params,
    }),
  createService: (gameId: number, data: AwdpServiceCreateModel, params: RequestParams = {}) =>
    api.request<AwdpServiceViewModel, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/services`,
      method: 'POST',
      body: data,
      type: json,
      format: 'json',
      ...params,
    }),
  updateService: (serviceId: number, data: AwdpServiceUpdateModel, params: RequestParams = {}) =>
    api.request<AwdpServiceViewModel, RequestResponse>({
      path: `/api/admin/awdp/services/${serviceId}`,
      method: 'PUT',
      body: data,
      type: json,
      format: 'json',
      ...params,
    }),
  deleteService: (serviceId: number, params: RequestParams = {}) =>
    api.request<void, RequestResponse>({
      path: `/api/admin/awdp/services/${serviceId}`,
      method: 'DELETE',
      ...params,
    }),
  startGame: (gameId: number, params: RequestParams = {}) =>
    api.request<RequestResponse, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/start`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  stopGame: (gameId: number, params: RequestParams = {}) =>
    api.request<RequestResponse, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/stop`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getStatus: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpGameStatusModel, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/status`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getInstances: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpServiceStatusModel[], RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/instances`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  resetInstance: (instanceId: number, params: RequestParams = {}) =>
    api.request<AwdpInstanceActionModel, RequestResponse>({
      path: `/api/admin/awdp/instances/${instanceId}/reset`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  recoverInstance: (instanceId: number, params: RequestParams = {}) =>
    api.request<AwdpInstanceActionModel, RequestResponse>({
      path: `/api/admin/awdp/instances/${instanceId}/recover`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getPatches: (gameId: number, count = 50, skip = 0, params: RequestParams = {}) =>
    api.request<AwdpArrayResponse<AwdpPatchSubmissionViewModel>, RequestResponse>({
      path: `/api/admin/awdp/games/${gameId}/patches`,
      method: 'GET',
      query: { count, skip },
      format: 'json',
      ...params,
    }),
}

export const awdpPlayerApi = {
  getStatus: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpGameStatusModel, RequestResponse>({
      path: `/api/awdp/games/${gameId}/status`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getInstances: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpTeamServiceStatus[], RequestResponse>({
      path: `/api/awdp/games/${gameId}/instances`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  submitFlag: (gameId: number, flag: string, params: RequestParams = {}) =>
    api.request<AwdpSubmitResultModel, RequestResponse>({
      path: `/api/awdp/games/${gameId}/flags`,
      method: 'POST',
      body: { flag },
      type: json,
      format: 'json',
      ...params,
    }),
  submitPatch: (gameId: number, serviceId: number, file: File, params: RequestParams = {}) =>
    api.request<AwdpPatchSubmissionViewModel, RequestResponse>({
      path: `/api/awdp/games/${gameId}/patches`,
      method: 'POST',
      body: { serviceId, file },
      type: form,
      format: 'json',
      ...params,
    }),
  resetInstance: (instanceId: number, params: RequestParams = {}) =>
    api.request<AwdpInstanceActionModel, RequestResponse>({
      path: `/api/awdp/instances/${instanceId}/reset`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  recoverInstance: (instanceId: number, params: RequestParams = {}) =>
    api.request<AwdpInstanceActionModel, RequestResponse>({
      path: `/api/awdp/instances/${instanceId}/recover`,
      method: 'POST',
      format: 'json',
      ...params,
    }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpScoreboardItem[], RequestResponse>({
      path: `/api/awdp/games/${gameId}/scoreboard`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
  getAttackLogs: (gameId: number, count = 50, skip = 0, params: RequestParams = {}) =>
    api.request<AwdpArrayResponse<AwdpAttackLogItem>, RequestResponse>({
      path: `/api/awdp/games/${gameId}/attacklogs`,
      method: 'GET',
      query: { count, skip },
      format: 'json',
      ...params,
    }),
  getPatchStatus: (gameId: number, params: RequestParams = {}) =>
    api.request<AwdpPatchStatusItem[], RequestResponse>({
      path: `/api/awdp/games/${gameId}/patchstatus`,
      method: 'GET',
      format: 'json',
      ...params,
    }),
}
