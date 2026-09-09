import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export async function readVmDiagnostics(runtimeId: string, assetId: number) {
  const item = parse.record(await runtimeJsonClient.get(
    `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/vm-diagnostics`
  ), 'VM diagnostics')
  return { state: parse.string(item.state, 'state'), nativeId: parse.string(item.nativeId, 'nativeId'),
    observedAt: parse.number(item.observedAt, 'observedAt') }
}

export async function readAssetDiagnostics(runtimeId: string, assetId: number, tail: number) {
  const item = parse.record(await runtimeJsonClient.get(
    `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/diagnostics?tail=${tail}`
  ), '资产诊断')
  return {
    state: parse.string(item.state, 'state'),
    paused: parse.boolean(item.paused, 'paused'),
    exitCode: parse.number(item.exitCode, 'exitCode'),
    restartCount: parse.number(item.restartCount, 'restartCount'),
    startedAt: parse.string(item.startedAt, 'startedAt'),
    finishedAt: parse.string(item.finishedAt, 'finishedAt'),
    logs: parse.string(item.logs, 'logs'),
    truncated: parse.boolean(item.truncated, 'truncated'),
    logsError: item.logsError == null ? null : parse.string(item.logsError, 'logsError'),
    observedAt: parse.number(item.observedAt, 'observedAt'),
  }
}
