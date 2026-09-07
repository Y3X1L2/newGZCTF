import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChallengeCategory, ChallengeType, Difficulty, EnvironmentType, NetworkMode } from '@Api'
import { ExerciseAdminDraft, exerciseAdminApi, normalizeExerciseRuntime } from './exerciseAdminApi'

function containerDraft(): ExerciseAdminDraft {
  return {
    title: 'lab', content: 'content', category: ChallengeCategory.Web,
    type: ChallengeType.StaticContainer, difficulty: Difficulty.Baby,
    credit: false, isEnabled: true, tags: ['web'], hints: [], flags: [], attachment: null,
    containerImage: 'registry.test/lab:v1', memoryLimit: 128, storageLimit: 512,
    cpuCount: 2, exposePort: 8080, networkMode: NetworkMode.Open,
    environment: EnvironmentType.Docker, imageTemplateId: 12, flagTemplate: 'flag{[GUID]}', submissionLimit: 0,
  }
}

describe('exerciseAdminApi', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads the management route and retains disabled exercises', async () => {
    const rows = [{ id: 7, title: 'draft', isEnabled: false }]
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => rows })
    vi.stubGlobal('fetch', fetchMock)

    expect(await exerciseAdminApi.list()).toEqual(rows)
    expect(fetchMock).toHaveBeenCalledWith('/api/exercise/manage', undefined)
  })

  it('does not turn a denied management response into an empty list', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 403, json: async () => ({ title: 'Forbidden' }),
    }))

    await expect(exerciseAdminApi.list()).rejects.toThrow('Forbidden')
  })

  it('clears hidden runtime bindings when a container becomes an attachment exercise', () => {
    const draft = { ...containerDraft(), type: ChallengeType.StaticAttachment }
    const normalized = normalizeExerciseRuntime(draft)

    expect(normalized).toMatchObject({
      title: 'lab', type: ChallengeType.StaticAttachment, environment: EnvironmentType.None,
      imageTemplateId: null, containerImage: null, exposePort: null,
      memoryLimit: null, storageLimit: null, cpuCount: null,
    })
    expect(draft.imageTemplateId).toBe(12)
    expect(normalized.tags).toBe(draft.tags)
  })

  it('does not change a container exercise runtime', () => {
    const draft = containerDraft()
    expect(normalizeExerciseRuntime(draft)).toBe(draft)
  })
})
