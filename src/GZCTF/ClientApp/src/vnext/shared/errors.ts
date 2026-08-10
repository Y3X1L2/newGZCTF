export function errorMessage(error: unknown, fallback: string) {
  if (error && typeof error === 'object') {
    const value = error as Record<string, unknown>
    const code = typeof value.code === 'string' ? value.code : undefined
    const codeMessages: Record<string, string> = {
      bootstrap_secret_required: '创建试运行前必须填写所有必填的运行时密钥。',
      release_not_ready: '该发布版本尚未满足运行条件，请先处理页面列出的阻断项。',
      release_not_found: '找不到所选发布版本，可能已被归档。',
      runtime_cleanup_pending: '该运行环境正在清理，请等待清理完成后再操作。',
    }
    if (code && codeMessages[code]) return codeMessages[code]
    if (typeof value.title === 'string' && value.title) return value.title
    const response = value.response
    if (response && typeof response === 'object') {
      const data = (response as Record<string, unknown>).data
      if (data && typeof data === 'object') {
        const payload = data as Record<string, unknown>
        if (typeof payload.code === 'string' && codeMessages[payload.code]) return codeMessages[payload.code]
        if (typeof payload.title === 'string' && payload.title) return payload.title
        if (typeof payload.message === 'string' && payload.message) return payload.message
      }
    }
  }
  if (error instanceof Error && error.message) return error.message
  return fallback
}
