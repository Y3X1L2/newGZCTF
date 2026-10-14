import type { AdminLogEntry } from '../api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export function adminLogKey(log: AdminLogEntry) {
  if (log.id !== undefined && log.id > 0) return `id:${log.id}`
  return adminLogFactKey(log)
}

function adminLogFactKey(log: AdminLogEntry) {
  return JSON.stringify([
    log.time,
    log.name,
    log.ip,
    log.level,
    log.msg,
    log.status,
    log.eventCode,
    log.correlationId,
    log.resourceType,
    log.resourceId,
    log.workerNodeId,
    log.deploymentTicketId,
    log.actorKind,
    log.actorUserId,
    log.logger,
    log.outcome,
    log.errorCategory,
    log.errorCode,
    log.retryable,
    log.detail,
  ])
}

export function mergeAdminLogs(...collections: AdminLogEntry[][]) {
  const unique = new Map<string, AdminLogEntry>()
  const persistedFacts = new Set(
    collections
      .flat()
      .filter((log) => (log.id ?? 0) > 0)
      .map(adminLogFactKey)
  )
  for (const log of collections.flat()) {
    if (!(log.id && log.id > 0) && persistedFacts.has(adminLogFactKey(log))) continue
    unique.set(adminLogKey(log), log)
  }
  return [...unique.values()].sort((left, right) => right.time - left.time)
}

export type LogText = (key: string) => string

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function displayName(value?: string | null) {
  const name = value?.trim()
  return name && !uuid.test(name) ? name : null
}

function recordedName(log: AdminLogEntry, value: string | null, field: string, text: LogText) {
  return value && log.currentDisplayNames?.includes(field) ? `${value} (${text('current_name')})` : value
}

export function adminLogNode(log: AdminLogEntry, text: LogText) {
  return recordedName(log, displayName(log.workerNodeName), 'node', text)
}

export function adminLogActor(log: AdminLogEntry, text: LogText) {
  const kind = log.actorKind?.toLowerCase() || 'unknown'
  const name = recordedName(log, displayName(log.actorDisplayName || log.name), 'actor', text)
  if (kind === 'user') return name || text('actor.user')
  if (kind === 'system') return text('actor.system')
  if (kind === 'anonymous') return text('actor.anonymous')
  if (kind === 'agent') return text('actor.agent')
  if (kind === 'apitoken')
    return name && !['Anonymous', 'System'].includes(name)
      ? `${name} · ${text('actor.api_token')}`
      : text('actor.api_token')
  if (name && !['Anonymous', 'System'].includes(name)) return `${name} · ${text('actor.kind_unknown')}`
  return text('actor.unknown')
}

const resourceTypes: Record<string, string> = {
  'worker-node': 'node',
  'image-template': 'image',
  'deployment-ticket': 'deployment',
  'deployment-request': 'deployment',
  user: 'user',
  team: 'team',
  game: 'game',
  challenge: 'challenge',
  course: 'course',
  container: 'container',
  vm: 'vm',
  'vm-instance': 'vm',
  'teamlab-runtime': 'teamlab',
}

export function adminLogObject(log: AdminLogEntry, text: LogText) {
  const type = resourceTypes[log.resourceType?.toLowerCase() || '']
  const name =
    recordedName(log, displayName(log.resourceDisplayName), 'resource', text) ||
    (type === 'node' ? adminLogNode(log, text) : null)
  if (type) return name ? `${text(`resource.${type}`)} · ${name}` : text(`resource_missing.${type}`)
  return name || text(log.resourceId ? 'object.unnamed' : 'object.unrecorded')
}

export function adminLogIp(log: AdminLogEntry, text: LogText) {
  if (log.ip) return log.ip
  return text(['system', 'agent'].includes(log.actorKind?.toLowerCase() || '') ? 'ip_background' : 'ip_unrecorded')
}

export function adminLogErrorCategory(log: AdminLogEntry, text: LogText) {
  if (!log.errorCategory) return null
  const key = `error.${log.errorCategory.toLowerCase()}`
  const label = text(key)
  return label === key || label === `logs.${key}` ? log.errorCategory : label
}

export function adminLogTitle(log: AdminLogEntry, text: LogText) {
  if (log.eventCode?.startsWith('audit.admin.mutation.') && typeof log.detail?.operation === 'string') {
    const key = `operations.${log.detail.operation.replaceAll('.', '_')}`
    const title = text(key)
    if (title !== key && title !== `logs.${key}`) return title
  }
  if (log.eventCode) {
    const key = `events.${log.eventCode.replaceAll('.', '_')}`
    const title = text(key)
    if (title !== key && title !== `logs.${key}`) return title
    return text('event.unknown')
  }
  return log.msg?.trim() || text('event.legacy')
}

export function adminLogResult(log: AdminLogEntry, text: LogText) {
  const historicalSystemDefault =
    !log.actorKind || log.actorKind.toLowerCase() === 'unknown'
      ? log.name === 'System' && log.status?.toLowerCase() === 'success'
      : false
  // Historical structured events inherited a default Success, which is not an event outcome.
  const value =
    (log.outcome || (log.eventCode || historicalSystemDefault ? null : log.status))?.toLowerCase() || 'unknown'
  const normalized =
    (
      {
        success: 'succeeded',
        exit: 'cancelled',
        denied: 'blocked',
        unhealthy: 'failed',
        degraded: 'degraded',
      } as Record<string, string>
    )[value] || value
  const known = [
    'started',
    'pending',
    'blocked',
    'succeeded',
    'failed',
    'cancelled',
    'recovered',
    'observed',
    'degraded',
    'unknown',
  ].includes(normalized)
  const tone: AdminStatusTone = ['failed', 'blocked'].includes(normalized)
    ? 'danger'
    : ['succeeded', 'recovered'].includes(normalized)
      ? 'success'
      : ['pending', 'degraded'].includes(normalized)
        ? 'warning'
        : normalized === 'started'
          ? 'info'
          : 'neutral'
  return { label: known ? text(`result.${normalized}`) : log.outcome || log.status || text('result.unknown'), tone }
}

export function adminLogSeverity(log: AdminLogEntry, text: LogText) {
  const level = log.level?.toLowerCase() || 'unknown'
  const normalized =
    ({ info: 'information', warn: 'warning', fatal: 'critical' } as Record<string, string>)[level] || level
  const tone: AdminStatusTone = ['error', 'critical'].includes(normalized)
    ? 'danger'
    : normalized === 'warning'
      ? 'warning'
      : normalized === 'information'
        ? 'info'
        : 'neutral'
  return {
    tone,
    label: ['information', 'warning', 'error', 'critical', 'debug', 'trace', 'unknown'].includes(normalized)
      ? text(`level.${normalized}`)
      : log.level,
  }
}

export function adminLogHasDiagnostics(log: AdminLogEntry) {
  return Boolean(
    log.errorCategory ||
    log.errorCode ||
    ['failed', 'blocked', 'degraded', 'denied', 'unhealthy'].includes(
      (log.outcome || log.status || '').toLowerCase()
    ) ||
    ['error', 'fatal', 'critical'].includes((log.level || '').toLowerCase())
  )
}

export function adminLogMatches(log: AdminLogEntry, query: string, identity: string, text: LogText) {
  const includes = (values: (string | null | undefined)[], search: string) =>
    !search.trim() || values.some((value) => value?.toLowerCase().includes(search.trim().toLowerCase()))
  return (
    includes([log.name, log.actorDisplayName, log.ip, adminLogActor(log, text)], identity) &&
    includes(
      [
        log.msg,
        log.eventCode,
        log.resourceDisplayName,
        log.resourceId,
        log.workerNodeName,
        log.logger,
        log.errorCode,
        adminLogTitle(log, text),
        adminLogObject(log, text),
        adminLogResult(log, text).label,
      ],
      query
    )
  )
}
