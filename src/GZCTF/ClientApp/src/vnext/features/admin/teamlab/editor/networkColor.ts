export const networkColorSlots = ['brand', 'info', 'success', 'warning'] as const
export type NetworkColorSlot = (typeof networkColorSlots)[number]

export function networkColorSlot(networkKey: string): NetworkColorSlot {
  let hash = 2_166_136_261
  for (let index = 0; index < networkKey.length; index += 1) {
    hash ^= networkKey.charCodeAt(index)
    hash = Math.imul(hash, 16_777_619)
  }
  return networkColorSlots[(hash >>> 0) % networkColorSlots.length]
}
