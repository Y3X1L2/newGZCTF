import { describe, expect, it } from 'vitest'
import { externalEntryHref, safeResourceHref } from './urls'

describe('externalEntryHref', () => {
  it('adds an HTTP scheme to host and port entries', () => {
    expect(externalEntryHref('203.195.157.191:30001')).toBe('http://203.195.157.191:30001')
  })

  it('preserves explicit schemes', () => {
    expect(externalEntryHref('https://example.test/instance')).toBe('https://example.test/instance')
  })

  it('rejects unsupported schemes and malformed entries', () => {
    expect(externalEntryHref('javascript://alert(1)')).toBeNull()
    expect(externalEntryHref('data:text/html,test')).toBeNull()
    expect(externalEntryHref('bad host:32768')).toBeNull()
  })

  it('normalizes protocol-relative entries', () => {
    expect(externalEntryHref('//example.test:32768')).toBe('http://example.test:32768')
  })
})

describe('safeResourceHref', () => {
  it('allows HTTP, HTTPS, and site-relative resources', () => {
    expect(safeResourceHref('https://example.test/file.zip')).toBe('https://example.test/file.zip')
    expect(safeResourceHref('/api/assets/file.zip')).toBe('/api/assets/file.zip')
  })

  it('rejects executable and non-web schemes', () => {
    expect(safeResourceHref('javascript:alert(1)')).toBeNull()
    expect(safeResourceHref('file:///etc/passwd')).toBeNull()
    expect(safeResourceHref('//untrusted.test/file')).toBeNull()
    expect(safeResourceHref('https://')).toBeNull()
  })
})
