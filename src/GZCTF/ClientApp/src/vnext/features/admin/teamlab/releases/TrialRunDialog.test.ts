import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTrialIdempotencyKey } from './TrialRunDialog'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('createTrialIdempotencyKey', () => {
  it('uses random bytes when randomUUID is unavailable', () => {
    vi.stubGlobal('crypto', {
      getRandomValues: (bytes: Uint8Array) => {
        bytes.fill(0xab)
        return bytes
      },
    })

    expect(createTrialIdempotencyKey()).toBe(`teamlab-trial-${'ab'.repeat(16)}`)
  })

  it('keeps a non-cryptographic compatibility fallback for old browsers', () => {
    vi.stubGlobal('crypto', undefined)
    vi.spyOn(Date, 'now').mockReturnValue(1_000)
    vi.spyOn(Math, 'random').mockReturnValue(0.5)

    expect(createTrialIdempotencyKey()).toBe('teamlab-trial-rs-i')
  })
})
