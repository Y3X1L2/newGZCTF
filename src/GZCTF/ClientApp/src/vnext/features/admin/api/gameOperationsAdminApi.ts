import api, {
  Division,
  DivisionCreateModel,
  DivisionEditModel,
  GameNotice,
  GameNoticeModel,
  GamePhase,
  ParticipationEditModel,
  ParticipationInfoModel,
} from '@Api'
import {
  contractFailure,
  isBoolean,
  isNumber,
  isOptionalString,
  isRecord,
  isString,
  parseRecordArray,
} from './contractParsers'

export interface AdminGamePhase {
  id: number
  gameId: number
  name: string
  startTime: number
  endTime: number
  ctfEnabled: boolean
  securityPolicy?: string | null
}

export type AdminGamePhaseWrite = Pick<AdminGamePhase, 'name' | 'startTime' | 'endTime' | 'ctfEnabled'>

function isAdminGamePhase(value: unknown): value is AdminGamePhase {
  return (
    isRecord(value) &&
    isNumber(value.id) &&
    isNumber(value.gameId) &&
    isString(value.name) &&
    isNumber(value.startTime) &&
    isNumber(value.endTime) &&
    isBoolean(value.ctfEnabled) &&
    isOptionalString(value.securityPolicy)
  )
}

export function parseAdminGamePhase(value: unknown, label = 'Game phase') {
  if (!isAdminGamePhase(value)) return contractFailure(label, value)
  return value
}

export function parseAdminGamePhases(value: unknown) {
  return parseRecordArray(value, isAdminGamePhase, 'Game phase list')
}

function phasePayload(gameId: number, payload: AdminGamePhaseWrite): GamePhase {
  return { gameId, ...payload }
}

export const gameOperationsKeys = {
  phases: (gameId: number) => ['vnext:admin:game-phases', gameId] as const,
  divisions: (gameId: number) => ['vnext:admin:game-divisions', gameId] as const,
  participations: (gameId: number) => ['vnext:admin:game-participations', gameId] as const,
  notices: (gameId: number) => ['vnext:admin:game-notices', gameId] as const,
}

export const gameOperationsAdminApi = {
  async listPhases(gameId: number) {
    const response = await api.gamePhase.gamePhaseList(gameId)
    return parseAdminGamePhases(response.data as unknown)
  },

  async createPhase(gameId: number, payload: AdminGamePhaseWrite) {
    const response = await api.gamePhase.gamePhaseCreate(gameId, phasePayload(gameId, payload))
    return parseAdminGamePhase(response.data as unknown, 'Create game phase')
  },

  async updatePhase(gameId: number, phaseId: number, payload: AdminGamePhaseWrite) {
    const response = await api.gamePhase.gamePhaseUpdate(phaseId, phasePayload(gameId, payload))
    return parseAdminGamePhase(response.data as unknown, 'Update game phase')
  },

  async removePhase(phaseId: number) {
    await api.gamePhase.gamePhaseDelete(phaseId)
  },

  async listDivisions(gameId: number) {
    return (await api.edit.editGetDivisions(gameId)).data
  },

  async createDivision(gameId: number, payload: DivisionCreateModel) {
    return (await api.edit.editCreateDivision(gameId, payload)).data
  },

  async updateDivision(gameId: number, divisionId: number, payload: DivisionEditModel) {
    return (await api.edit.editUpdateDivision(gameId, divisionId, payload)).data
  },

  async removeDivision(gameId: number, divisionId: number) {
    await api.edit.editDeleteDivision(gameId, divisionId)
  },

  async listParticipations(gameId: number) {
    return (await api.game.gameParticipations(gameId)).data
  },

  async updateParticipation(participationId: number, payload: ParticipationEditModel) {
    await api.admin.adminParticipation(participationId, payload)
  },

  async listNotices(gameId: number) {
    return (await api.edit.editGetGameNotices(gameId)).data
  },

  async createNotice(gameId: number, payload: GameNoticeModel) {
    return (await api.edit.editAddGameNotice(gameId, payload)).data
  },

  async updateNotice(gameId: number, noticeId: number, payload: GameNoticeModel) {
    return (await api.edit.editUpdateGameNotice(gameId, noticeId, payload)).data
  },

  async removeNotice(gameId: number, noticeId: number) {
    await api.edit.editDeleteGameNotice(gameId, noticeId)
  },
}

export type AdminGameDivision = Division
export type AdminGameParticipation = ParticipationInfoModel
export type AdminGameNotice = GameNotice
