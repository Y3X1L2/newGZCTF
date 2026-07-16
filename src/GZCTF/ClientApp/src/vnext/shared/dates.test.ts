import { describe, expect, it } from 'vitest'
import { localDateKey } from './dates'

describe('localDateKey', () => {
  it('uses local calendar fields instead of converting through UTC', () => {
    const date = new Date(2026, 6, 16, 0, 0, 0)
    expect(localDateKey(date)).toBe('2026-07-16')
  })

  it('rejects invalid dates', () => {
    expect(localDateKey(new Date(Number.NaN))).toBe('')
  })
})
