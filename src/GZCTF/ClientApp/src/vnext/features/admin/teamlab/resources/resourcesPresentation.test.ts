import { describe, expect, it } from 'vitest'
import { formatBytes, summarizeLinkPolicyParameters, toAdminDate } from './resourcesPresentation'

describe('resourcesPresentation', () => {
  it('summarizes link policy parameters for table cells', () => {
    expect(summarizeLinkPolicyParameters('latency', { delayMillis: 120 })).toBe('delayMillis: 120')
    expect(summarizeLinkPolicyParameters('access-rule', { direction: 'inbound', action: 'deny' })).toBe(
      'direction: inbound，action: deny'
    )
    expect(summarizeLinkPolicyParameters('link-break', null)).toBe('全链路中断')
    expect(summarizeLinkPolicyParameters('latency', null)).toBe('—')
    expect(summarizeLinkPolicyParameters('latency', {})).toBe('—')
  })

  it('converts backend ISO timestamps to admin formatter millis', () => {
    expect(toAdminDate('2026-08-17T08:00:00Z')).toBe(Date.parse('2026-08-17T08:00:00Z'))
    expect(toAdminDate(null)).toBeNull()
    expect(toAdminDate('not-a-date')).toBeNull()
  })

  it('formats byte sizes with bounded precision', () => {
    expect(formatBytes(0)).toBe('—')
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2 KiB')
    expect(formatBytes(5 * 1024 * 1024)).toBe('5 MiB')
  })
})
