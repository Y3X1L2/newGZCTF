import type { AdminLogEntry } from '../api'
import { adminLogKey } from './adminLogPresentation'

export const MAX_BUFFERED_ADMIN_LOGS = 500

export function appendAdminLogBuffer(
  current: AdminLogEntry[],
  message: AdminLogEntry,
  limit = MAX_BUFFERED_ADMIN_LOGS
) {
  const key = adminLogKey(message)
  if (current.some((item) => adminLogKey(item) === key)) {
    return { items: current, dropped: 0 }
  }

  const boundedLimit = Math.max(1, limit)
  const next = [message, ...current]
  return {
    items: next.slice(0, boundedLimit),
    dropped: Math.max(0, next.length - boundedLimit),
  }
}
