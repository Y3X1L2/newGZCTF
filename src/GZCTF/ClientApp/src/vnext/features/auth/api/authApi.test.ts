import { describe, expect, it } from 'vitest'
import { normalizeAccountCapabilities } from './authApi'

describe('normalizeAccountCapabilities', () => {
  it('requires both the IAM flag and a usable entry URL', () => {
    expect(normalizeAccountCapabilities({ portalSso: { enabled: true } }).portalSso.enabled).toBe(false)
    expect(
      normalizeAccountCapabilities({
        allowRegister: true,
        passwordRecoveryAvailable: true,
        portalSso: { enabled: true, entryUrl: 'http://portal.local/demo/dashboard' },
      })
    ).toEqual({
      allowPasswordLogin: true,
      allowRegister: true,
      passwordRecoveryAvailable: true,
      emailConfirmationRequired: false,
      portalSso: { enabled: true, entryUrl: 'http://portal.local/demo/dashboard' },
    })
  })
})
