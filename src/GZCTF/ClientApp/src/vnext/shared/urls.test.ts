import { describe, expect, it } from 'vitest'
import { externalEntryHref } from './urls'

describe('externalEntryHref', () => {
  it('adds an HTTP scheme to host and port entries', () => {
    expect(externalEntryHref('203.195.157.191:30001')).toBe('http://203.195.157.191:30001')
  })

  it('preserves explicit schemes', () => {
    expect(externalEntryHref('https://example.test/instance')).toBe('https://example.test/instance')
  })
})
