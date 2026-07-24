import { beforeEach, describe, expect, it } from 'vitest'
import {
  clientConfigStorageKey,
  defaultClientConfig,
  parseStoredClientConfig,
  readStoredClientConfig,
  storeClientConfig,
} from './clientConfigStorage'

describe('client config storage', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('falls back when stored data is malformed', () => {
    expect(parseStoredClientConfig('{')).toEqual(defaultClientConfig)
  })

  it('merges older partial values with current defaults', () => {
    const parsed = parseStoredClientConfig(JSON.stringify({ title: 'Test platform' }))

    expect(parsed.title).toBe('Test platform')
    expect(parsed.defaultLifetime).toBe(defaultClientConfig.defaultLifetime)
    expect(parsed.portMapping).toBe(defaultClientConfig.portMapping)
  })

  it('persists and restores a valid config', () => {
    const config = { ...defaultClientConfig, title: 'Stored platform', defaultLifetime: 180 }

    storeClientConfig(config)

    expect(window.localStorage.getItem(clientConfigStorageKey)).toBe(JSON.stringify(config))
    expect(readStoredClientConfig()).toEqual(config)
  })
})
