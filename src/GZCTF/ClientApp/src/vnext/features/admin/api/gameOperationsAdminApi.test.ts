import { describe, expect, it } from 'vitest'
import { parseAdminGamePhase, parseAdminGamePhases } from './gameOperationsAdminApi'

const phase = {
  id: 3,
  gameId: 7,
  name: '攻击阶段',
  startTime: 1_784_000_000_000,
  endTime: 1_784_003_600_000,
  ctfEnabled: true,
  securityPolicy: null,
}

describe('gameOperationsAdminApi phase parser', () => {
  it('accepts the deployed JSON phase contract', () => {
    expect(parseAdminGamePhase(phase)).toEqual(phase)
    expect(parseAdminGamePhases([phase])).toEqual([phase])
  })

  it('rejects the incorrect generated Blob shape', () => {
    expect(() => parseAdminGamePhases(new Blob(['[]'], { type: 'application/json' }))).toThrow(
      'Game phase list returned an unexpected response shape.'
    )
  })
})
