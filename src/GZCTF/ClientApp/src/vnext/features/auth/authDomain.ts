export type PendingReason = 'approval' | 'email-verification' | 'unknown'

export function safeReturnUrl(value: string | null | undefined, fallback = '/') {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return fallback

  try {
    const parsed = new URL(value, window.location.origin)
    return parsed.origin === window.location.origin ? `${parsed.pathname}${parsed.search}${parsed.hash}` : fallback
  } catch {
    return fallback
  }
}

export function normalizeEncodedParameter(value: string | null) {
  return value?.replaceAll(' ', '+') ?? null
}

export function decodeEmailParameter(value: string | null) {
  const normalized = normalizeEncodedParameter(value)
  if (!normalized) return null

  try {
    const decoded = window.atob(normalized)
    return decoded.includes('@') ? decoded : null
  } catch {
    return null
  }
}

export function maskEmail(value: string | null) {
  if (!value) return null
  const [name, domain] = value.split('@')
  if (!name || !domain) return null
  return `${name.slice(0, 2)}${'*'.repeat(Math.max(2, Math.min(name.length - 2, 6)))}@${domain}`
}

export function pendingReason(value: string | null): PendingReason {
  if (value === 'approval' || value === 'email-verification') return value
  return 'unknown'
}

export function loginValidation(userName: string, password: string) {
  if (!userName.trim()) return '请输入用户名或邮箱。'
  if (!password) return '请输入密码。'
  return null
}

export function registrationValidation(userName: string, email: string, password: string, confirmation: string) {
  if (!/^\S+@\S+\.\S+$/.test(email.trim())) return '请输入有效的邮箱地址。'
  if (userName.trim().length < 3 || userName.trim().length > 15) return '用户名长度应为 3-15 个字符。'
  if (password.length < 6) return '密码至少需要 6 个字符。'
  if (password !== confirmation) return '两次输入的密码不一致。'
  return null
}

export function passwordResetValidation(password: string, confirmation: string) {
  if (password.length < 6) return '密码至少需要 6 个字符。'
  if (password !== confirmation) return '两次输入的密码不一致。'
  return null
}
