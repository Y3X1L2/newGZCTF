import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export const deviceHealthApi = {
  read: async (id: string) => parse.array(await runtimeJsonClient.get(`/api/admin/teamlab/runtimes/${encodeURIComponent(id)}/device-health`), '设备监督', value => {
    const item = parse.record(value, '设备')
    const observation = item.observation == null ? null : parse.record(item.observation, '监督结果')
    return {
      assetId: parse.number(item.assetId, 'assetId'), name: parse.string(item.name, 'name'),
      generation: parse.number(item.generation, 'generation'),
      nextProbeAt: parse.nullableNumber(item.nextProbeAt, 'nextProbeAt'),
      observation: observation ? {
        status: parse.string(observation.status, 'status'), observedAt: parse.number(observation.observedAt, 'observedAt'),
        errorCode: parse.nullableString(observation.errorCode, 'errorCode'),
        counters: observation.protocolCounters == null ? [] : Object.entries(parse.record(observation.protocolCounters, 'counters'))
          .map(([type, count]) => ({ type, count: parse.number(count, 'count') })),
      } : null,
    }
  }),
}
