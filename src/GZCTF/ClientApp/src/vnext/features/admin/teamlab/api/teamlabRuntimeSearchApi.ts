import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export interface RuntimeSearchFilters {
  search: string; status: string; node: string; generation: string; releaseId: string; createdById: string; errorsOnly: boolean
}
export async function searchRuntimes(filters: RuntimeSearchFilters, after: string | null) {
  const query = new URLSearchParams({ limit: '20' })
  for (const [key, value] of Object.entries(filters)) if (value !== '' && value !== false) query.set(key, String(value).trim())
  if (after) query.set('after', after)
  const page = parse.record(await runtimeJsonClient.get(`/api/admin/teamlab/runtimes/search?${query}`), 'runtime search')
  return { nextCursor: parse.nullableString(page.nextCursor, 'nextCursor'),
    items: parse.array(page.items, 'items', (value, label) => {
      const item = parse.record(value, label)
      return { id: parse.string(item.id, 'id'), topologyId: parse.nullableString(item.topologyId, 'topologyId'),
        releaseId: parse.string(item.releaseId, 'releaseId'), reference: parse.nullableString(item.reference, 'reference'),
        generation: parse.number(item.generation, 'generation'), status: parse.string(item.status, 'status'),
        createdById: parse.nullableString(item.createdById, 'createdById'), createdAt: parse.number(item.createdAt, 'createdAt'),
        assetCount: parse.number(item.assetCount, 'assetCount'), hasError: parse.boolean(item.hasError, 'hasError') }
    }) }
}
