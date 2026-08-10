import { runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import type {
  TeamLabRemoteAccessAvailability,
  TeamLabRemoteConnect,
  TeamLabRemoteProtocol,
  TeamLabRemoteSession,
  TeamLabRemoteSessionStatus,
} from './teamlabRemoteAccessContracts'
import { teamLabParsing as parse } from './teamlabParsers'

const root = '/api/admin/teamlab'
const protocols = { ContainerTerminal: 'containerTerminal', Ssh: 'ssh', Rdp: 'rdp', containerTerminal: 'containerTerminal', ssh: 'ssh', rdp: 'rdp', 0: 'containerTerminal', 1: 'ssh', 2: 'rdp' } as const
const statuses = { Creating: 'creating', Ready: 'ready', Connected: 'connected', Ending: 'ending', Ended: 'ended', Failed: 'failed', 0: 'creating', 1: 'ready', 2: 'connected', 3: 'ending', 4: 'ended', 5: 'failed' } as const

function availability(value: unknown): TeamLabRemoteAccessAvailability {
  const item = parse.record(value, '远程运维可用性')
  return {
    assetId: parse.number(item.assetId, '远程运维可用性.assetId'),
    assetName: parse.string(item.assetName, '远程运维可用性.assetName'),
    protocol: item.protocol == null ? null : parse.enumValue<TeamLabRemoteProtocol>(item.protocol, protocols, '远程运维可用性.protocol'),
    available: parse.boolean(item.available, '远程运维可用性.available'),
    unavailableReason: parse.nullableString(item.unavailableReason, '远程运维可用性.unavailableReason'),
  }
}

function availabilityList(value: unknown): readonly TeamLabRemoteAccessAvailability[] {
  return parse.array(value, '远程运维可用性列表', (entry, _label) => availability(entry))
}

function session(value: unknown): TeamLabRemoteSession {
  const item = parse.record(value, '远程运维会话')
  return {
    id: parse.string(item.id, '远程运维会话.id'),
    runtimeId: parse.string(item.runtimeId, '远程运维会话.runtimeId'),
    assetId: parse.number(item.assetId, '远程运维会话.assetId'),
    assetName: parse.string(item.assetName, '远程运维会话.assetName'),
    protocol: parse.enumValue<TeamLabRemoteProtocol>(item.protocol, protocols, '远程运维会话.protocol'),
    status: parse.enumValue<TeamLabRemoteSessionStatus>(item.status, statuses, '远程运维会话.status'),
    reason: parse.string(item.reason, '远程运维会话.reason'),
    createdAt: parse.number(item.createdAt, '远程运维会话.createdAt'),
    expiresAt: parse.number(item.expiresAt, '远程运维会话.expiresAt'),
    connectedAt: parse.nullableNumber(item.connectedAt, '远程运维会话.connectedAt'),
    endedAt: parse.nullableNumber(item.endedAt, '远程运维会话.endedAt'),
    endReason: parse.nullableString(item.endReason, '远程运维会话.endReason'),
  }
}

function connect(value: unknown): TeamLabRemoteConnect {
  const item = parse.record(value, '远程运维连接')
  return { url: parse.string(item.url, '远程运维连接.url'), expiresAt: parse.number(item.expiresAt, '远程运维连接.expiresAt') }
}

export function createTeamLabRemoteAccessApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async getAvailability(runtimeId: string, assetId: number) {
      return availability(await client.get(`${root}/runtimes/${runtimeId}/assets/${assetId}/remote-access`))
    },
    async getAvailabilityBatch(runtimeId: string) {
      return availabilityList(await client.get(`${root}/runtimes/${runtimeId}/remote-access`))
    },
    async createSession(runtimeId: string, assetId: number, reason: string) {
      return session(await client.postJson(`${root}/runtimes/${runtimeId}/assets/${assetId}/remote-sessions`, { reason }))
    },
    async getSession(sessionId: string) {
      return session(await client.get(`${root}/remote-sessions/${sessionId}`))
    },
    async connect(sessionId: string) {
      return connect(await client.get(`${root}/remote-sessions/${sessionId}/connect`))
    },
    async end(sessionId: string) {
      await client.delete(`${root}/remote-sessions/${sessionId}`)
    },
  }
}

export const teamLabRemoteAccessApi = createTeamLabRemoteAccessApi()
