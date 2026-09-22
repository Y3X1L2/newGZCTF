import { beforeEach, describe, expect, it, vi } from 'vitest'
import api, { LeagueRegistrationState } from '@Api'
import { leagueApi } from './leagueApi'

vi.mock('@Api', async (importOriginal) => {
  const original = await importOriginal<typeof import('@Api')>()
  return {
    ...original,
    default: { leagueMatches: {
      leagueMatchesUpdate: vi.fn(),
      leagueMatchesSelectTeams: vi.fn(),
      leagueMatchesReview: vi.fn(),
      leagueMatchesList: vi.fn(),
    } },
  }
})

beforeEach(() => vi.resetAllMocks())

describe('league U1 API', () => {
  it('preserves the edit revision so a concurrent registration cannot be silently overwritten', async () => {
    const draft = { name: '联赛', initialCoins: 0, topologyId: null, releaseId: null }
    const conflict = { response: { status: 409, data: { code: 'league_revision_conflict' } } }
    vi.mocked(api.leagueMatches.leagueMatchesUpdate).mockRejectedValue(conflict)
    await expect(leagueApi.update('match', 3, draft)).rejects.toBe(conflict)
    expect(api.leagueMatches.leagueMatchesUpdate).toHaveBeenCalledExactlyOnceWith('match', { revision: 3, draft })
  })

  it('preserves explicit seat order instead of sorting team IDs', async () => {
    const result = { match: { revision: 5 } }
    vi.mocked(api.leagueMatches.leagueMatchesSelectTeams).mockResolvedValue({ data: result } as never)
    await expect(leagueApi.selectTeams('match', 4, 22, 11)).resolves.toBe(result)
    expect(api.leagueMatches.leagueMatchesSelectTeams).toHaveBeenCalledExactlyOnceWith('match', {
      revision: 4, firstTeamId: 22, secondTeamId: 11,
    })
  })

  it('returns the updated projection after rejecting a registration, including cleared seats', async () => {
    const result = { match: { revision: 6 }, registrations: [{ teamId: 11, selected: false, seat: null }] }
    vi.mocked(api.leagueMatches.leagueMatchesReview).mockResolvedValue({ data: result } as never)
    await expect(leagueApi.review('match', 11, false)).resolves.toBe(result)
    expect(api.leagueMatches.leagueMatchesReview).toHaveBeenCalledExactlyOnceWith('match', 11, {
      state: LeagueRegistrationState.Rejected,
    })
  })

  it('passes the server cursor without inventing page numbers or a total', async () => {
    const result = { items: [], nextCursor: null }
    vi.mocked(api.leagueMatches.leagueMatchesList).mockResolvedValue({ data: result } as never)
    await expect(leagueApi.list('cursor')).resolves.toBe(result)
    expect(api.leagueMatches.leagueMatchesList).toHaveBeenCalledExactlyOnceWith({ after: 'cursor', limit: 30 })
  })
})
