export type TeamTab = 'overview' | 'members' | 'requests' | 'settings'

export type TeamFeedback = { tone: 'success' | 'danger'; message: string }

export const validTeamTabs = new Set<TeamTab>(['overview', 'members', 'requests', 'settings'])

export function parseTeamId(value: string | null) {
  const id = Number(value)
  return Number.isInteger(id) && id > 0 ? id : null
}
