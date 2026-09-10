import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'
import type { AssetControlAction } from './teamlabAssetControlApi'

export interface RuntimeDifference {
  assetId: number | null
  workerNodeId: string | null
  resourceKind: string
  name: string
  expectedState: string
  actualState: string | null
  difference: string
  suggestedAction: AssetControlAction | null
}
const base = (id: string) => `/api/admin/teamlab/runtimes/${encodeURIComponent(id)}/differences`
const actions = new Set<string>(['start', 'stop', 'pause', 'resume', 'rebuild'])
export const differencesApi = {
  preview: async (runtimeId: string) => {
    const data = parse.record(await runtimeJsonClient.get(base(runtimeId)), '运行差异')
    const items = parse.array(data.items, 'items', value => {
      const item = parse.record(value, '差异项')
      const action = parse.nullableString(item.suggestedAction, 'suggestedAction')
      if (action !== null && !actions.has(action)) throw new Error('无法识别的修复动作，请刷新页面。')
      return {
        assetId: item.assetId === null ? null : parse.number(item.assetId, 'assetId'),
        workerNodeId: parse.nullableString(item.workerNodeId, 'workerNodeId'),
        resourceKind: parse.string(item.resourceKind, 'resourceKind'), name: parse.string(item.name, 'name'),
        expectedState: parse.string(item.expectedState, 'expectedState'), actualState: parse.nullableString(item.actualState, 'actualState'),
        difference: parse.string(item.difference, 'difference'), suggestedAction: action as AssetControlAction | null,
      } satisfies RuntimeDifference
    })
    return { generation: parse.number(data.generation, 'generation'), operationInProgress: parse.boolean(data.operationInProgress, 'operationInProgress'), items }
  },
  repair: async (runtimeId: string, assetId: number, generation: number, action: AssetControlAction, reason: string) => {
    const data = parse.record(await runtimeJsonClient.postJson(`${base(runtimeId)}/assets/${assetId}/repair`,
      { generation, action, reason, confirmed: true }), '修复任务')
    return parse.string(data.ticketId, 'ticketId')
  },
}
