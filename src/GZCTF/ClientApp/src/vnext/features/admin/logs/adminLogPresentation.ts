import type { AdminLogEntry } from '../api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export function adminLogKey(log: AdminLogEntry) {
  if (log.id !== undefined) return `id:${log.id}`
  return ['fact', log.time, log.name, log.ip, log.level, log.msg, log.status].join('|')
}

export function mergeAdminLogs(...collections: AdminLogEntry[][]) {
  const unique = new Map<string, AdminLogEntry>()
  for (const log of collections.flat()) unique.set(adminLogKey(log), log)
  return [...unique.values()].sort((left, right) => right.time - left.time)
}

export function adminLogLevelMeta(level: string | null) {
  const normalized = level?.toLowerCase() ?? ''
  if (normalized === 'error' || normalized === 'fatal') return { label: level || 'Error', tone: 'danger' as AdminStatusTone }
  if (normalized === 'warning' || normalized === 'warn') return { label: level || 'Warning', tone: 'warning' as AdminStatusTone }
  if (normalized === 'information' || normalized === 'info') return { label: level || 'Information', tone: 'info' as AdminStatusTone }
  return { label: level || 'Unknown', tone: 'neutral' as AdminStatusTone }
}

export function adminLogStatusMeta(status: string | null) {
  const normalized = status?.toLowerCase() ?? ''
  if (['success'].includes(normalized)) return { label: status || '成功', tone: 'success' as AdminStatusTone }
  if (['failed', 'denied', 'unhealthy'].includes(normalized)) return { label: status || '失败', tone: 'danger' as AdminStatusTone }
  if (['pending', 'degraded'].includes(normalized)) return { label: status || '处理中', tone: 'warning' as AdminStatusTone }
  if (normalized === 'exit') return { label: '已取消', tone: 'neutral' as AdminStatusTone }
  return { label: status || '—', tone: 'neutral' as AdminStatusTone }
}

export function adminLogSource(log: AdminLogEntry) {
  return log.workerNodeName || log.name || log.ip || '平台'
}

export function adminLogResource(log: AdminLogEntry) {
  return log.resourceDisplayName || log.resourceId || log.eventCode || '—'
}
