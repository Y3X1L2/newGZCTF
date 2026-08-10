import { describe, expect, it, vi } from 'vitest'
import { createTeamLabGameAdminApi, parseTeamLabGameTargetPage } from '../../api/teamlabGameAdminApi'
import type { RuntimeJsonClient } from '../../api/runtimeJsonClient'

function client() {
  const unexpected = vi.fn(async () => { throw new Error('Unexpected API call') })
  return {
    get: vi.fn(),
    postJson: vi.fn(),
    postForm: unexpected,
    putJson: vi.fn(),
    patchJson: unexpected,
    delete: vi.fn(async () => undefined),
  } satisfies RuntimeJsonClient
}

const binding = {
  gameId: 8,
  topologyId: '019f0000-0000-7000-8000-000000000001',
  activeReleaseId: '019f0000-0000-7000-8000-000000000002',
  maxResetCount: 2,
  objectiveRevision: 0,
  objectives: [],
}
const rollout = {
  id: '019f0000-0000-7000-8000-000000000003',
  releaseId: binding.activeReleaseId,
  status: 'ready',
  preparationRequested: true,
  desiredAccessOpen: false,
  drainRequested: false,
  counts: { total: 1, pending: 0, provisioning: 0, ready: 1, accessOpen: 0, failed: 0, draining: 0, destroyed: 0, paused: 0 },
  preparedAt: 1_784_918_400_000,
  accessOpenedAt: null,
  drainingAt: null,
  completedAt: null,
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
  error: null,
}

describe('teamLabGameAdminApi', () => {
  it('parses the game binding and rollout projection at the adapter boundary', async () => {
    const transport = client()
    transport.get.mockResolvedValue({ binding, rollout })
    const api = createTeamLabGameAdminApi(transport)

    await expect(api.state(8)).resolves.toMatchObject({ binding: { gameId: 8 }, rollout: { status: 'ready' } })
    expect(transport.get).toHaveBeenCalledWith('/api/admin/pentest/games/8/teamlab')
  })

  it('uses the deployed lifecycle endpoints without generated API or direct fetch', async () => {
    const transport = client()
    transport.putJson.mockResolvedValue(binding)
    transport.postJson
      .mockResolvedValueOnce(binding)
      .mockResolvedValueOnce(rollout)
      .mockResolvedValueOnce({ ...rollout, desiredAccessOpen: true })
      .mockResolvedValueOnce({ ...rollout, status: 'draining', drainRequested: true })
      .mockResolvedValueOnce({ runtimeId: '019f0000-0000-7000-8000-000000000004', reused: false })
      .mockResolvedValueOnce({ message: 'cleaned' })
    const api = createTeamLabGameAdminApi(transport)

    await api.bind(8, binding.topologyId)
    await api.replaceObjectives(8, { revision: 0, maxResetCount: 2, objectives: [] })
    await api.activateRelease(8, binding.activeReleaseId)
    await api.prepare(8)
    await api.setAccess(8, true)
    await api.drain(8)
    await api.rebuildTeam(8, 12)
    await api.cleanupTeam(8, 12)

    expect(transport.putJson).toHaveBeenCalledWith('/api/admin/pentest/games/8/binding', { topologyId: binding.topologyId })
    expect(transport.putJson).toHaveBeenCalledWith('/api/admin/pentest/games/8/objectives', { revision: 0, maxResetCount: 2, objectives: [] })
    expect(transport.postJson).toHaveBeenNthCalledWith(1, `/api/admin/pentest/games/8/releases/${binding.activeReleaseId}/activate`)
    expect(transport.postJson).toHaveBeenNthCalledWith(2, '/api/admin/pentest/games/8/teamlab/prepare')
    expect(transport.postJson).toHaveBeenNthCalledWith(3, '/api/admin/pentest/games/8/teamlab/access/open')
    expect(transport.postJson).toHaveBeenNthCalledWith(4, '/api/admin/pentest/games/8/teamlab/drain')
    expect(transport.postJson).toHaveBeenNthCalledWith(5, '/api/admin/pentest/games/8/teams/12/rebuild')
    expect(transport.postJson).toHaveBeenNthCalledWith(6, '/api/admin/pentest/games/8/teams/12/cleanup')
  })

  it('derives a numeric team id only from the penetration target subject contract', () => {
    const page = { items: [{ id: 'target-1', externalSubject: 'team:12', displayName: 'Alpha', runtimeId: null, status: 'pending', operationId: null, runtimeStatus: null, runtimeStage: null, createdAt: 1, updatedAt: 2, error: null }], nextCursor: null }
    expect(parseTeamLabGameTargetPage(page).items[0].teamId).toBe(12)
    expect(() => parseTeamLabGameTargetPage({ ...page, items: [{ ...page.items[0], externalSubject: 'user:12' }] })).toThrow('TeamLab rollout target page.items[0].externalSubject')
  })

  it('uses the dedicated operator grant contract for non-owner remote access', async () => {
    const transport = client()
    transport.get.mockResolvedValue([{ userId: '019f0000-0000-7000-8000-000000000005', userName: 'operator', displayName: '运维员', viewAssets: true, operateAssets: false, updatedAt: 1 }])
    transport.putJson.mockResolvedValue(undefined)
    const api = createTeamLabGameAdminApi(transport)
    const userId = '019f0000-0000-7000-8000-000000000005'

    await expect(api.operators(8)).resolves.toEqual([expect.objectContaining({ userId, operateAssets: false })])
    await api.setOperator(8, userId, { viewAssets: true, operateAssets: true })
    await api.deleteOperator(8, userId)

    expect(transport.get).toHaveBeenCalledWith('/api/admin/pentest/games/8/teamlab/operators')
    expect(transport.putJson).toHaveBeenCalledWith(`/api/admin/pentest/games/8/teamlab/operators/${userId}`, { viewAssets: true, operateAssets: true })
    expect(transport.delete).toHaveBeenCalledWith(`/api/admin/pentest/games/8/teamlab/operators/${userId}`)
  })
})
