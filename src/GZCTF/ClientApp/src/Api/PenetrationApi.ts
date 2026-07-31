import api, { AnswerResult, RequestParams, RequestResponse } from '@Api'

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
  objectiveRevision: number
  objectives: PenetrationObjectiveModel[]
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

const request = api.request

export const penetrationAdminApi = {
  getBinding: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationGameLabBindingModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/binding`, method: 'GET', format: 'json', ...params }),
  getScoreboard: (gameId: number, params: RequestParams = {}) =>
    request<PenetrationScoreboardItemModel[], RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/scoreboard`, method: 'GET', format: 'json', ...params }),
  getSubmissions: (gameId: number, count = 50, skip = 0, params: RequestParams = {}) =>
    request<PenetrationSubmissionPageModel, RequestResponse>({ path: `/api/admin/pentest/games/${gameId}/submissions`, method: 'GET', query: { count, skip }, format: 'json', ...params }),
}
