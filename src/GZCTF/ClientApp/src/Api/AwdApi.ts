import api, { ContentType } from '@Api'

export enum AwdRoundStatus {
  Preparing = 'Preparing',
  Running = 'Running',
  Finished = 'Finished',
}

export interface AwdServiceCreateModel {
  name: string
  imageName: string
  exposePort?: number
  checkerScript?: string | null
  checkerEntrypoint?: string | null
  attackPoints?: number
  slaPoints?: number
  maxAttackPerRound?: number
  roundDurationMinutes?: number
  totalRounds?: number
}

export interface AwdServiceUpdateModel extends AwdServiceCreateModel {}

export interface AwdServiceViewModel {
  id: number
  name: string
  imageName: string
  exposePort: number
  attackPoints: number
  slaPoints: number
  roundDurationMinutes: number
  totalRounds: number
}

export interface AwdSubmitModel {
  flag: string
  targetTeamId?: number
  serviceId?: number
}

export interface AwdGameStatusModel {
  gameId: number
  currentRound: number
  roundStartTime: number
  roundDurationMinutes: number
  status: AwdRoundStatus
}

export interface TeamServiceStatus {
  instanceId?: number
  teamId: number
  teamName: string
  ipAddress?: string | null
  port?: number | null
  lastCheckerStatus?: string | null
  isRunning: boolean
}

export interface AwdScoreboardItem {
  rank: number
  teamId: number
  teamName: string
  ctfScore: number
  awdScore: number
  totalScore: number
  attackScore: number
  slaScore: number
  defenseLost: number
}

export interface AwdAttackLogItem {
  time: number
  attackerTeam: string
  victimTeam: string
  serviceName: string
  points: number
}

const request = api.request

export const awdAdminApi = {
  getServices: (gameId: number) =>
    request<AwdServiceViewModel[], unknown>({
      path: `/api/admin/awd/games/${gameId}/services`,
      method: 'GET',
    }),

  createService: (gameId: number, data: AwdServiceCreateModel) =>
    request<AwdServiceViewModel, unknown>({
      path: `/api/admin/awd/games/${gameId}/services`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateService: (serviceId: number, data: AwdServiceUpdateModel) =>
    request<AwdServiceViewModel, unknown>({
      path: `/api/admin/awd/services/${serviceId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  deleteService: (serviceId: number) =>
    request<void, unknown>({
      path: `/api/admin/awd/services/${serviceId}`,
      method: 'DELETE',
    }),

  startGame: (gameId: number) =>
    request<void, unknown>({
      path: `/api/admin/awd/games/${gameId}/start`,
      method: 'POST',
    }),

  stopGame: (gameId: number) =>
    request<void, unknown>({
      path: `/api/admin/awd/games/${gameId}/stop`,
      method: 'POST',
    }),

  resetInstance: (instanceId: number) =>
    request<void, unknown>({
      path: `/api/admin/awd/instances/${instanceId}/reset`,
      method: 'POST',
    }),

  getInstances: (gameId: number) =>
    request<TeamServiceStatus[], unknown>({
      path: `/api/admin/awd/games/${gameId}/instances`,
      method: 'GET',
    }),

  getGameStatus: (gameId: number) =>
    request<AwdGameStatusModel, unknown>({
      path: `/api/admin/awd/games/${gameId}/status`,
      method: 'GET',
    }),
}

export const awdPlayerApi = {
  getGameStatus: (gameId: number) =>
    request<AwdGameStatusModel, unknown>({
      path: `/api/awd/games/${gameId}/status`,
      method: 'GET',
    }),

  getMyInstances: (gameId: number) =>
    request<TeamServiceStatus[], unknown>({
      path: `/api/awd/games/${gameId}/instances`,
      method: 'GET',
    }),

  submitFlag: (gameId: number, data: AwdSubmitModel) =>
    request<{ title: string; status: number }, unknown>({
      path: `/api/awd/games/${gameId}/submit`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  getScoreboard: (gameId: number) =>
    request<AwdScoreboardItem[], unknown>({
      path: `/api/awd/games/${gameId}/scoreboard`,
      method: 'GET',
    }),

  getAttackLogs: (gameId: number, count?: number, skip?: number) =>
    request<AwdAttackLogItem[], unknown>({
      path: `/api/awd/games/${gameId}/attack-logs`,
      method: 'GET',
      query: { count, skip },
    }),
}
