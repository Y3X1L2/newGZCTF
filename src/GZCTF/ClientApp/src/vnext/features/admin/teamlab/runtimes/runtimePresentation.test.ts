import { describe, expect, it } from 'vitest'
import { runtimeRefreshInterval } from './runtimePresentation'

describe('runtimeRefreshInterval', () => {
  it('polls active deployment states and slows down stable running state', () => {
    expect(runtimeRefreshInterval('deploying')).toBe(2_500)
    expect(runtimeRefreshInterval('running')).toBe(8_000)
  })

  it('stops polling terminal persisted states', () => {
    expect(runtimeRefreshInterval('failed')).toBe(0)
    expect(runtimeRefreshInterval('destroyed')).toBe(0)
  })

  it('keeps polling a paused runtime so resume is observed', () => {
    expect(runtimeRefreshInterval('paused')).toBe(2_500)
  })
})
