import api, { TeamUpdateModel } from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function useCurrentTeams(enabled: boolean) {
  return api.team.useTeamGetTeamsInfo(swrOptions, enabled)
}

export function useTeamDetails(teamId: number, enabled: boolean) {
  return api.team.useTeamGetBasicInfo(teamId, swrOptions, enabled)
}

export function useTeamJoinRequests(teamId: number, enabled: boolean) {
  return api.team.useTeamGetJoinRequests(teamId, swrOptions, enabled)
}

export function useTeamInviteCode(teamId: number, enabled: boolean) {
  return api.team.useTeamInviteCode(teamId, swrOptions, enabled)
}

export function useTeamSearch(hint: string, enabled: boolean) {
  return api.team.useTeamSearch({ hint }, { ...swrOptions, keepPreviousData: true }, enabled)
}

export const teamApi = {
  async create(data: TeamUpdateModel) {
    const response = await api.team.teamCreateTeam(data)
    return response.data
  },
  async joinByInviteCode(token: string) {
    await api.team.teamAccept(token)
  },
  async requestToJoin(teamId: number, message: string) {
    await api.team.teamCreateJoinRequest(teamId, { message })
  },
  async update(teamId: number, data: TeamUpdateModel) {
    await api.team.teamUpdateTeam(teamId, data)
  },
  async uploadAvatar(teamId: number, file: File) {
    await api.team.teamAvatar(teamId, { file })
  },
  async reviewJoinRequest(teamId: number, requestId: number, accepted: boolean) {
    await api.team.teamReviewJoinRequest(teamId, requestId, { accepted })
  },
  async kickMember(teamId: number, userId: string) {
    await api.team.teamKickUser(teamId, userId)
  },
  async transferCaptain(teamId: number, userId: string) {
    await api.team.teamTransfer(teamId, { newCaptainId: userId })
  },
  async leave(teamId: number) {
    await api.team.teamLeave(teamId)
  },
  async delete(teamId: number) {
    await api.team.teamDeleteTeam(teamId)
  },
  async refreshInviteCode(teamId: number) {
    await api.team.teamUpdateInviteToken(teamId)
  },
}
