import { describe, expect, it } from 'vitest'
import { numericEnumValue, patchQuery, positiveInteger } from './useAdminQueryState'

describe('admin query state helpers', () => {
  it('uses the fallback for missing or invalid page numbers', () => {
    expect(positiveInteger(null, 1)).toBe(1)
    expect(positiveInteger('0', 1)).toBe(1)
    expect(positiveInteger('-2', 1)).toBe(1)
    expect(positiveInteger('2.5', 1)).toBe(1)
    expect(positiveInteger('3', 1)).toBe(3)
  })

  it('does not interpret a missing enum filter as zero', () => {
    expect(numericEnumValue(null, [0, 1] as const)).toBeUndefined()
    expect(numericEnumValue('', [0, 1] as const)).toBeUndefined()
    expect(numericEnumValue('0', [0, 1] as const)).toBe(0)
    expect(numericEnumValue('2', [0, 1] as const)).toBeUndefined()
  })

  it('patches filters without dropping unrelated query state', () => {
    const current = new URLSearchParams('q=linux&page=4&status=0')
    expect(patchQuery(current, { q: 'windows' }).toString()).toBe('q=windows&status=0')
    expect(patchQuery(current, { status: null }).toString()).toBe('q=linux')
    expect(patchQuery(current, { page: 2 }, false).toString()).toBe('q=linux&page=2&status=0')
  })
})
