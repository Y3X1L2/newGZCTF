export function safeResourceHref(value?: string | null) {
  const normalized = value?.trim()
  if (!normalized) return null
  if (/^\/(?!\/)/.test(normalized)) return normalized
  if (!/^https?:\/\//i.test(normalized)) return null
  try {
    const parsed = new URL(normalized)
    return parsed.hostname && !parsed.username && !parsed.password ? normalized : null
  } catch {
    return null
  }
}

export function externalEntryHref(entry?: string | null) {
  const normalized = entry?.trim()
  if (!normalized || /\s/.test(normalized)) return null
  const candidate = /^https?:\/\//i.test(normalized)
    ? normalized
    : /^\/\//.test(normalized)
      ? `http:${normalized}`
      : null
  if (!candidate && /^[a-z][a-z\d+.-]*:/i.test(normalized)) return null
  const href = candidate ?? `http://${normalized}`
  try {
    const parsed = new URL(href)
    return parsed.hostname && !parsed.username && !parsed.password ? href : null
  } catch {
    return null
  }
}
