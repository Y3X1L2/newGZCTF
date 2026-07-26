import { runtimeJsonClient, type RuntimeJsonClient } from '../../../admin/api/runtimeJsonClient'
import {
  parseTeamLabPlayerAccessGrant,
  parseTeamLabPlayerResetReceipt,
  parseTeamLabPlayerSubmissionResult,
  parseTeamLabPlayerWorkspace,
} from './teamlabPlayerParsers'
import { projectTeamLabPlayerWorkspace } from './teamlabPlayerProjection'

function routeId(value: number, field: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) throw new TypeError(`${field} must be a positive integer.`)
  return value
}

export const teamLabPlayerKeys = {
  workspace: (gameId: number) => ['vnext:games:teamlab:workspace', gameId] as const,
}

export function createTeamLabPlayerApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async getWorkspace(gameId: number) {
      const id = routeId(gameId, 'gameId')
      const response = await client.get(`/api/pentest/games/${id}/workspace`)
      return projectTeamLabPlayerWorkspace(parseTeamLabPlayerWorkspace(response))
    },

    async createAccessGrant(gameId: number) {
      const id = routeId(gameId, 'gameId')
      return parseTeamLabPlayerAccessGrant(await client.postJson(`/api/pentest/games/${id}/access-grants`))
    },

    async resetWorkspace(gameId: number) {
      const id = routeId(gameId, 'gameId')
      return parseTeamLabPlayerResetReceipt(await client.postJson(`/api/pentest/games/${id}/reset`))
    },

    async submitObjective(gameId: number, objectiveId: number, flag: string) {
      const id = routeId(gameId, 'gameId')
      const targetId = routeId(objectiveId, 'objectiveId')
      return parseTeamLabPlayerSubmissionResult(
        await client.postJson(`/api/pentest/games/${id}/submit`, { objectiveId: targetId, flag })
      )
    },
  }
}

export const teamLabPlayerApi = createTeamLabPlayerApi()
