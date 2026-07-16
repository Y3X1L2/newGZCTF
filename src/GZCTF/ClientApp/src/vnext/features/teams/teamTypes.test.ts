import { describe, expect, it } from 'vitest'
import { parseTeamId, validTeamTabs } from './teamTypes'

describe('team route state', () => {
  it('accepts only positive integer team identifiers', () => {
    expect(parseTeamId('7')).toBe(7)
    expect(parseTeamId('0')).toBeNull()
    expect(parseTeamId('-1')).toBeNull()
    expect(parseTeamId('3.5')).toBeNull()
    expect(parseTeamId('not-a-team')).toBeNull()
    expect(parseTeamId(null)).toBeNull()
  })

  it('keeps the supported workspace tabs explicit', () => {
    expect([...validTeamTabs]).toEqual(['overview', 'members', 'requests', 'settings'])
  })
})
