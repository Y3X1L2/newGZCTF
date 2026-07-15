import { describe, expect, it, vi } from 'vitest'
import { createSWRConfig } from './swr'

describe('createSWRConfig', () => {
  it('uses isolated in-memory caches and disables global polling', () => {
    const first = createSWRConfig()
    const second = createSWRConfig()
    const firstCache = first.provider?.(new Map())
    const secondCache = second.provider?.(new Map())

    expect(first.refreshInterval).toBe(0)
    expect(first.keepPreviousData).toBe(false)
    expect(firstCache).toBeInstanceOf(Map)
    expect(secondCache).toBeInstanceOf(Map)
    expect(firstCache).not.toBe(secondCache)
  })

  it('does not retry authorization and exhausted failures', () => {
    const config = createSWRConfig()
    const revalidate = vi.fn()
    const retry = config.onErrorRetry
    if (!retry) throw new Error('onErrorRetry is not configured')
    const normalizedConfig = config as unknown as Parameters<typeof retry>[2]

    retry({ response: { status: 401 } }, 'account', normalizedConfig, revalidate, { retryCount: 0, dedupe: true })
    retry(new Error('offline'), 'account', normalizedConfig, revalidate, { retryCount: 3, dedupe: true })

    expect(revalidate).not.toHaveBeenCalled()
  })
})
