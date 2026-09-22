import api, { LeagueRegistrationState } from '@Api'
import type { LeagueDraftModel } from '@Api'

// All U1 writes return the server projection. Callers must replace their cached
// detail with it, including the revision changed by registration and review.
export const leagueApi = {
  async list(after?: string) {
    return (await api.leagueMatches.leagueMatchesList({ after, limit: 30 })).data
  },
  async detail(matchId: string) {
    return (await api.leagueMatches.leagueMatchesGet(matchId)).data
  },
  async create(draft: LeagueDraftModel) {
    return (await api.leagueMatches.leagueMatchesCreate(draft)).data
  },
  async update(matchId: string, revision: number, draft: LeagueDraftModel) {
    return (await api.leagueMatches.leagueMatchesUpdate(matchId, { revision, draft })).data
  },
  async register(matchId: string, teamId: number) {
    return (await api.leagueMatches.leagueMatchesRegister(matchId, { teamId })).data
  },
  async review(matchId: string, teamId: number, approved: boolean) {
    return (await api.leagueMatches.leagueMatchesReview(matchId, teamId, {
      state: approved ? LeagueRegistrationState.Approved : LeagueRegistrationState.Rejected,
    })).data
  },
  async selectTeams(matchId: string, revision: number, firstTeamId: number, secondTeamId: number) {
    return (await api.leagueMatches.leagueMatchesSelectTeams(matchId, { revision, firstTeamId, secondTeamId })).data
  },
}
