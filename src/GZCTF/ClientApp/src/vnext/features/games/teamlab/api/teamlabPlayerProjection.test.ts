import { describe, expect, it } from 'vitest'
import type { TeamLabPlayerWorkspace } from './teamlabPlayerContracts'
import { projectTeamLabPlayerWorkspace } from './teamlabPlayerProjection'

const baseObjective = {
  description: null,
  category: 'General',
  attempts: 0,
  maxAttempts: 0,
  checkpoint: false,
} as const

describe('TeamLab player workspace projection', () => {
  it('groups objectives by the formally exposed asset key and resolves prerequisites', () => {
    const workspace: TeamLabPlayerWorkspace = {
      gameId: 1,
      teamId: 2,
      teamName: 'Blue Team',
      runtimeId: '019f0000-0000-7000-8000-000000000001',
      status: 'running',
      stage: 'ready',
      resetCount: 4,
      maxResetCount: 3,
      objectives: [
        {
          ...baseObjective,
          id: 1,
          key: 'entry',
          assetKey: 'edge',
          title: 'Entry',
          score: 100,
          solved: true,
          prerequisiteKeys: [],
        },
        {
          ...baseObjective,
          id: 2,
          key: 'pivot',
          assetKey: 'core',
          title: 'Pivot',
          score: 200,
          solved: false,
          attempts: 2,
          maxAttempts: 5,
          prerequisiteKeys: ['entry'],
        },
        {
          ...baseObjective,
          id: 3,
          key: 'domain',
          assetKey: 'core',
          title: 'Domain',
          score: 300,
          solved: false,
          prerequisiteKeys: ['pivot'],
        },
      ],
    }

    const result = projectTeamLabPlayerWorkspace(workspace)

    expect(result.resetAllowance).toEqual({ used: 4, limit: 3, remaining: 0 })
    expect(result).toMatchObject({ solvedCount: 1, objectiveCount: 3, totalScore: 600 })
    expect(result.objectives.map((item) => [item.key, item.available, item.remainingAttempts])).toEqual([
      ['entry', true, null],
      ['pivot', true, 3],
      ['domain', false, null],
    ])
    expect(result.targets).toEqual([
      expect.objectContaining({ assetKey: 'edge', solvedCount: 1, objectiveCount: 1, totalScore: 100 }),
      expect.objectContaining({ assetKey: 'core', solvedCount: 0, objectiveCount: 2, totalScore: 500 }),
    ])
  })
})
