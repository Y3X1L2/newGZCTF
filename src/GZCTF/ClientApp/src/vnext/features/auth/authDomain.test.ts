import { describe, expect, it, vi } from 'vitest'
import {
  decodeEmailParameter,
  loginValidation,
  maskEmail,
  normalizeEncodedParameter,
  passwordResetValidation,
  pendingReason,
  registrationValidation,
  safeReturnUrl,
} from './authDomain'

describe('authDomain', () => {
  it('accepts only local return URLs', () => {
    vi.stubGlobal('location', new URL('http://localhost:4320/account/login'))

    expect(safeReturnUrl('/games/23?tab=score')).toBe('/games/23?tab=score')
    expect(safeReturnUrl('https://example.com')).toBe('/')
    expect(safeReturnUrl('//example.com/path')).toBe('/')
    expect(safeReturnUrl('/\\example.com')).toBe('/')
  })

  it('normalizes legacy base64 query values and masks emails', () => {
    const encoded = window.btoa('person+ctf@example.com')
    expect(decodeEmailParameter(encoded.replaceAll('+', ' '))).toBe('person+ctf@example.com')
    expect(normalizeEncodedParameter('ab cd')).toBe('ab+cd')
    expect(maskEmail('person@example.com')).toBe('pe****@example.com')
  })

  it('validates authentication forms', () => {
    expect(loginValidation('', 'secret')).toBeTruthy()
    expect(registrationValidation('ab', 'invalid', '123', '321')).toBeTruthy()
    expect(passwordResetValidation('secret', 'different')).toBeTruthy()
    expect(registrationValidation('student', 'student@example.com', 'secret1', 'secret1')).toBeNull()
    expect(pendingReason('approval')).toBe('approval')
    expect(pendingReason('other')).toBe('unknown')
  })
})
