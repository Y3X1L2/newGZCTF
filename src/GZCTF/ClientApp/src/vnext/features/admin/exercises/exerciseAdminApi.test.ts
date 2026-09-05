import { afterEach, describe, expect, it, vi } from 'vitest'
import { exerciseAdminApi } from './exerciseAdminApi'

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
})
