import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export type AssetControlAction = 'start' | 'stop' | 'restart' | 'rebuild' | 'pause' | 'resume'
const base = (runtimeId: string, assetId: number) => `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/control`
const ticket = (value: unknown) => parse.string(parse.record(value, '资产操作票据').ticketId, 'ticketId')
export const assetControlApi = {
  availability: async (runtimeId: string, assetId: number) => {
    const item = parse.record(await runtimeJsonClient.get(base(runtimeId, assetId)), '资产操作权限')
    return { allowed: parse.boolean(item.allowed, 'allowed'), reason: parse.nullableString(item.reason, 'reason') }
  },
  submit: async (runtimeId: string, assetId: number, generation: number, action: AssetControlAction, reason: string) =>
    ticket(await runtimeJsonClient.postJson(base(runtimeId, assetId), { generation, action, reason, confirmed: true })),
  retry: async (runtimeId: string, assetId: number, ticketId: string) =>
    ticket(await runtimeJsonClient.postJson(`${base(runtimeId, assetId)}/${encodeURIComponent(ticketId)}/retry`, {})),
  task: async (runtimeId: string, assetId: number, ticketId: string) => {
    const item = parse.record(await runtimeJsonClient.get(`${base(runtimeId, assetId)}/${encodeURIComponent(ticketId)}`), '资产操作任务')
    return { id: parse.string(item.id, 'id'), status: parse.string(item.status, 'status'),
      stage: parse.nullableString(item.stage, 'stage'), errorCode: parse.nullableString(item.errorCode, 'errorCode'),
      canRetry: parse.boolean(item.canRetry, 'canRetry') }
  },
}
