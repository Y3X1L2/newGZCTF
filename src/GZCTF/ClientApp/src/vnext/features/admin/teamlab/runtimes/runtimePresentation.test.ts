import { describe, expect, it } from 'vitest'
import { runtimeRefreshInterval, runtimeStageLabel } from './runtimePresentation'

describe('runtimeRefreshInterval', () => {
  it('labels reported guest network stages and preserves unknown server stages', () => {
    expect(runtimeStageLabel('guest-ready')).toBe('等待来宾就绪')
    expect(runtimeStageLabel('guest-network-apply')).toBe('配置来宾网络')
    expect(runtimeStageLabel('guest-network-verify')).toBe('验证来宾网络')
    expect(runtimeStageLabel('future-stage')).toBe('future-stage')
  })
  it('polls active deployment states and slows down stable running state', () => {
    expect(runtimeRefreshInterval('deploying')).toBe(2_500)
    expect(runtimeRefreshInterval('running')).toBe(15_000)
  })

  it('stops polling terminal persisted states', () => {
    expect(runtimeRefreshInterval('failed')).toBe(0)
    expect(runtimeRefreshInterval('destroyed')).toBe(0)
  })

  it('keeps polling a paused runtime so resume is observed', () => {
    expect(runtimeRefreshInterval('paused')).toBe(0)
  })
})
