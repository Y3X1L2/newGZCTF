import { describe, expect, it } from 'vitest'
import type { TeamLabGameBinding } from '../../api/teamlabGameAdminApi'
import type { TeamLabTopologyAsset } from '../../teamlab/api/teamlabContracts'
import { objectivesFromBinding, toReplaceObjectivesRequest, validateObjectiveDrafts } from './gameObjectiveModel'

const asset = { key: 'web', name: 'Web', kind: 'docker' } as TeamLabTopologyAsset
const binding: TeamLabGameBinding = {
  gameId: 8,
  topologyId: 'topology-1',
  activeReleaseId: 'release-1',
  maxResetCount: 2,
  objectiveRevision: 4,
  objectives: [{
    id: 19,
    key: 'initial-access',
    assetKey: 'web',
    title: '初始访问',
    description: null,
    category: 'Web',
    score: 100,
    dynamic: false,
    maxAttempts: 5,
    visible: true,
    checkpoint: true,
    prerequisiteKeys: [],
    orderIndex: 0,
  }],
}

describe('game objective model', () => {
  it('preserves persisted identity while keeping secrets write-only', () => {
    const drafts = objectivesFromBinding(binding)
    const request = toReplaceObjectivesRequest(drafts, 3, binding.objectiveRevision)

    expect(request.revision).toBe(4)
    expect(request.maxResetCount).toBe(3)
    expect(request.objectives[0]).toMatchObject({ id: 19, key: 'initial-access', staticFlag: null, orderIndex: 0 })
    expect(validateObjectiveDrafts(drafts, [asset], 3)).toBeNull()
  })

  it('requires a new secret when changing a dynamic objective to a static objective', () => {
    const [draft] = objectivesFromBinding({
      ...binding,
      objectives: [{ ...binding.objectives[0], dynamic: true }],
    })

    expect(validateObjectiveDrafts([{ ...draft, dynamic: false }], [asset], 2)).toContain('必须填写 Flag')
    expect(validateObjectiveDrafts([{ ...draft, dynamic: false, staticFlag: 'flag{fixed}' }], [asset], 2)).toBeNull()
  })
})
