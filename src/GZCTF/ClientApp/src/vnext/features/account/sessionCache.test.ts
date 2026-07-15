import { describe, expect, it, vi } from 'vitest'
import { clearAccountSessionCache } from './sessionCache'

describe('clearAccountSessionCache', () => {
  it('clears the account entry and every global SWR key', async () => {
    const accountMutate = vi.fn().mockResolvedValue(undefined)
    const globalMutate = vi.fn().mockResolvedValue(undefined)

    await clearAccountSessionCache(accountMutate, globalMutate)

    const [accountUpdater, accountOptions] = accountMutate.mock.calls[0]
    expect(accountUpdater({ userName: 'old-user' })).toBeUndefined()
    expect(accountOptions).toEqual({ revalidate: false, populateCache: true })
    expect(globalMutate).toHaveBeenCalledOnce()
    const [matcher, updater, options] = globalMutate.mock.calls[0]
    expect(matcher('account/profile')).toBe(true)
    expect(matcher(['training', 3])).toBe(true)
    expect(updater({ private: true })).toBeUndefined()
    expect(options).toEqual({ revalidate: false, populateCache: true })
  })
})
