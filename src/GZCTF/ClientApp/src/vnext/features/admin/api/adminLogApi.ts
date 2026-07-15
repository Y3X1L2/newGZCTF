import { contractFailure, isNullableString, isNumber, isRecord } from './contractParsers'
import type { AdminLogEntry, AdminLogPage } from './contracts'
import { runtimeJsonClient, type RuntimeJsonClient } from './runtimeJsonClient'

export interface AdminLogQuery {
  level?: string
  count?: number
  offset?: number
  cursor?: string | null
}

function isLogEntry(value: unknown): value is AdminLogEntry {
  return (
    isRecord(value) &&
    (value.id === undefined || isNumber(value.id)) &&
    isNumber(value.time) &&
    isNullableString(value.name) &&
    isNullableString(value.level) &&
    isNullableString(value.ip) &&
    isNullableString(value.msg) &&
    isNullableString(value.status)
  )
}

function parseLogPage(value: unknown, offset: number): AdminLogPage {
  if (Array.isArray(value) && value.every(isLogEntry)) {
    return { contract: 'offset', items: value, nextCursor: null, offset }
  }
  if (
    isRecord(value) &&
    Array.isArray(value.items) &&
    value.items.every(isLogEntry) &&
    isNullableString(value.nextCursor)
  ) {
    return { contract: 'cursor', items: value.items, nextCursor: value.nextCursor, offset }
  }
  return contractFailure('Admin log list', value)
}

export function createAdminLogApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async list(query: AdminLogQuery = {}) {
      const offset = query.offset ?? 0
      const value = await client.get('/api/admin/logs', {
        level: query.level ?? 'All',
        count: query.count ?? 50,
        skip: offset,
        cursor: query.cursor,
      })
      return parseLogPage(value, offset)
    },
  }
}

export const adminLogApi = createAdminLogApi()
