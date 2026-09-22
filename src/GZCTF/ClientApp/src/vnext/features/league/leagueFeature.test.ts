import { afterEach, describe, expect, it, vi } from 'vitest'

afterEach(() => {
  vi.unstubAllEnvs()
  vi.resetModules()
})

describe('league navigation rollout', () => {
  it('keeps candidate navigation closed unless explicitly enabled at build time', async () => {
    vi.stubEnv('VITE_LEAGUE_ENABLED', undefined)
    vi.resetModules()
    expect((await import('./leagueFeature')).leagueEnabled).toBe(false)
    expect(
      (await import('../../app/shell/moduleRegistry')).platformModules.some((entry) => entry.id === 'league')
    ).toBe(false)
    vi.stubEnv('VITE_LEAGUE_ENABLED', 'true')
    vi.resetModules()
    const { platformModules, currentModule } = await import('../../app/shell/moduleRegistry')
    expect(platformModules.find((entry) => entry.id === 'league')?.route).toBe('/league')
    expect(currentModule('/league/new').id).toBe('league')
  })
})
