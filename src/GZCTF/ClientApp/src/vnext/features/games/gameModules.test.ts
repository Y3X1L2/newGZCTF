import { describe, expect, it } from 'vitest'
import { GameType } from '@Api'
import { gameModulesFor } from './gameModules'

describe('game module registry', () => {
  it('exposes the implemented AWDP workspace only for compatible game types', () => {
    expect(gameModulesFor(GameType.AWDP)).toEqual([expect.objectContaining({ id: 'awdp', implemented: true })])
    expect(gameModulesFor(GameType.Jeopardy).some((module) => module.id === 'awdp')).toBe(false)
    expect(gameModulesFor(GameType.Mixed).some((module) => module.id === 'awdp' && module.implemented)).toBe(true)
  })
})
