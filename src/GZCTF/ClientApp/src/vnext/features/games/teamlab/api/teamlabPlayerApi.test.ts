import { describe, expect, it, vi } from 'vitest'
import type { RuntimeJsonClient } from '../../../admin/api/runtimeJsonClient'
import { createTeamLabPlayerApi } from './teamlabPlayerApi'
import { TeamLabPlayerContractError } from './teamlabPlayerParsers'

const runtimeId = '019f0000-0000-7000-8000-000000000001'
const grantId = '019f0000-0000-7000-8000-000000000002'

function client(overrides: Partial<RuntimeJsonClient>): RuntimeJsonClient {
  const unexpected = async () => {
    throw new Error('Unexpected API call')
  }
  return {
    get: unexpected,
    postJson: unexpected,
    postForm: unexpected,
    putJson: unexpected,
    patchJson: unexpected,
    delete: unexpected,
    ...overrides,
  }
}

function workspace() {
  return {
    gameId: 12,
    teamId: 34,
    teamName: 'Red Team',
    runtimeId,
    status: 5,
    stage: 'ready',
    resetCount: 1,
    maxResetCount: 3,
    objectives: [
      {
        id: 7,
        key: 'foothold',
        assetKey: 'portal',
        title: 'Initial access',
        description: null,
        category: 'Web',
        score: 100,
        solved: false,
        attempts: 1,
        maxAttempts: 5,
        checkpoint: true,
        prerequisiteKeys: [],
      },
    ],
    shards: [{ workerNodeName: 'must-not-leak' }],
    assets: [{ primaryIp: '10.20.0.10' }],
  }
}

describe('TeamLab player API boundary', () => {
  it('strictly parses the formal workspace and excludes management-only fields', async () => {
    const get = vi.fn().mockResolvedValue(workspace())
    const api = createTeamLabPlayerApi(client({ get }))

    const result = await api.getWorkspace(12)

    expect(get).toHaveBeenCalledWith('/api/pentest/games/12/workspace')
    expect(result).toMatchObject({ status: 'running', resetAllowance: { used: 1, limit: 3, remaining: 2 } })
    expect(result.targets).toEqual([
      expect.objectContaining({ assetKey: 'portal', objectiveCount: 1, totalScore: 100 }),
    ])
    expect(result).not.toHaveProperty('shards')
    expect(result).not.toHaveProperty('assets')
  })

  it('rejects malformed unknown workspace data before it reaches a page', async () => {
    const api = createTeamLabPlayerApi(client({ get: vi.fn().mockResolvedValue({ ...workspace(), teamId: '34' }) }))

    await expect(api.getWorkspace(12)).rejects.toBeInstanceOf(TeamLabPlayerContractError)
  })

  it('uses the player access, reset and submit contracts without generated API calls', async () => {
    const postJson = vi
      .fn()
      .mockResolvedValueOnce({
        id: grantId,
        type: 'WireGuard',
        clientAddress: '10.99.0.2/32',
        endpoint: 'vpn.example:51820',
        allowedIps: '10.20.0.0/24',
        dns: '10.20.0.1',
        createdAt: 1_790_000_000_000,
        expiresAt: null,
        configurationDownloadUrl: `/api/pentest/games/12/access-grants/${grantId}/download?token=token`,
      })
      .mockResolvedValueOnce({ runtimeId })
      .mockResolvedValueOnce({ accepted: true, score: 100, message: 'Flag correct.' })
    const api = createTeamLabPlayerApi(client({ postJson }))

    await expect(api.createAccessGrant(12)).resolves.toMatchObject({ id: grantId, type: 'WireGuard' })
    await expect(api.resetWorkspace(12)).resolves.toEqual({ runtimeId })
    await expect(api.submitObjective(12, 7, 'encrypted-flag')).resolves.toEqual({
      accepted: true,
      score: 100,
      message: 'Flag correct.',
    })

    expect(postJson).toHaveBeenNthCalledWith(1, '/api/pentest/games/12/access-grants')
    expect(postJson).toHaveBeenNthCalledWith(2, '/api/pentest/games/12/reset')
    expect(postJson).toHaveBeenNthCalledWith(3, '/api/pentest/games/12/submit', {
      objectiveId: 7,
      flag: 'encrypted-flag',
    })
  })
})
