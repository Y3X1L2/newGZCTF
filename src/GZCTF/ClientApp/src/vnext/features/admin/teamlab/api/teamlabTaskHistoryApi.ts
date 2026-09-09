import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export async function readTaskHistory(runtimeId: string, generation: number | null, after?: string) {
  const query = new URLSearchParams({ limit: '20' })
  if (generation !== null) query.set('generation', String(generation))
  if (after) query.set('after', after)
  const page = parse.record(await runtimeJsonClient.get(`/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/tasks?${query}`), 'task history')
  return {
    nextCursor: parse.nullableString(page.nextCursor, 'nextCursor'),
    items: parse.array(page.items, 'items', (value, label) => {
      const item = parse.record(value, label)
      return {
        id: parse.string(item.id, 'id'), generation: parse.number(item.generation, 'generation'),
        operation: parse.string(item.operation, 'operation'), status: parse.string(item.status, 'status'),
        stage: parse.string(item.stage, 'stage'), operationId: parse.nullableString(item.operationId, 'operationId'),
        createdAt: parse.number(item.createdAt, 'createdAt'), startedAt: parse.nullableNumber(item.startedAt, 'startedAt'),
        completedAt: parse.nullableNumber(item.completedAt, 'completedAt'), errorCode: parse.nullableString(item.errorCode, 'errorCode'),
        blockedReasonCode: parse.nullableString(item.blockedReasonCode, 'blockedReasonCode'), retryable: parse.boolean(item.retryable, 'retryable'),
      }
    }),
  }
}
