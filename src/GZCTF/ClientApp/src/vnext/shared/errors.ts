export function errorMessage(error: unknown, fallback: string) {
  if (error && typeof error === 'object') {
    const value = error as Record<string, unknown>
    if (typeof value.title === 'string' && value.title) return value.title
    const response = value.response
    if (response && typeof response === 'object') {
      const data = (response as Record<string, unknown>).data
      if (data && typeof data === 'object') {
        const payload = data as Record<string, unknown>
        if (typeof payload.title === 'string' && payload.title) return payload.title
        if (typeof payload.message === 'string' && payload.message) return payload.message
      }
    }
  }
  if (error instanceof Error && error.message) return error.message
  return fallback
}
