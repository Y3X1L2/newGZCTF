import { describe, expect, it } from 'vitest'
import { boundedMap } from './boundedMap'

describe('boundedMap', () => {
  it('keeps result order while enforcing the concurrency limit', async () => {
    let active = 0
    let peak = 0
    const result = await boundedMap([1, 2, 3, 4, 5, 6], 3, async (value) => {
      active += 1
      peak = Math.max(peak, active)
      await new Promise((resolve) => setTimeout(resolve, 5))
      active -= 1
      return value * 2
    })

    expect(result).toEqual([2, 4, 6, 8, 10, 12])
    expect(peak).toBe(3)
  })

  it('rejects invalid limits before starting work', async () => {
    await expect(boundedMap([1], 0, async (value) => value)).rejects.toThrow('positive integer')
  })
})
