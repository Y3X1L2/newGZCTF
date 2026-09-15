import { describe, expect, it } from 'vitest'
import { currentModule } from './moduleRegistry'

describe('currentModule', () => {
  it.each([
    ['/admin/teamlab', 'teamlab'],
    ['/admin/teamlab/scenes/scene-a/design', 'teamlab'],
    ['/admin/users', 'admin'],
  ])('selects one most-specific module for %s', (pathname, expectedId) => {
    expect(currentModule(pathname).id).toBe(expectedId)
  })
})
