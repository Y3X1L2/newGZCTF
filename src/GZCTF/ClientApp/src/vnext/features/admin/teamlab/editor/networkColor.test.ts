import { describe, expect, it } from 'vitest'
import { networkColorSlot, networkColorSlots } from './networkColor'

describe('networkColor', () => {
  it('assigns a stable semantic color slot from the network key', () => {
    expect(networkColorSlot('entry-network')).toBe(networkColorSlot('entry-network'))
    expect(networkColorSlots).toContain(networkColorSlot('entry-network'))
    expect(
      new Set(['entry-network', 'data-network', 'management-network', 'dmz-network'].map(networkColorSlot)).size
    ).toBeGreaterThan(1)
  })
})
