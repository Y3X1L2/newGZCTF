import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export interface TeamLabServiceAccess {
  id: string
  runtimeId: string
  generation: number
  assetId: number
  assetName: string
  networkKey: string
  protocol: 'tcp' | 'udp'
  internalPort: number
  publicPort: number
  endpoint: string
  status: string
  lastError: string | null
  createdAt: number
  revokedAt: number | null
}

export interface CreateTeamLabServiceAccess {
  protocol: 'tcp' | 'udp'
  internalPort: number
  publicPort: number | null
  networkKey: string
}

const root = '/api/admin/teamlab/runtimes'

function parseServiceAccess(value: unknown, label = '服务开放记录'): TeamLabServiceAccess {
  const item = parse.record(value, label)
  const protocol = parse.string(item.protocol, '协议')
  if (protocol !== 'tcp' && protocol !== 'udp') throw new Error('服务开放记录包含未知协议。')
  return {
    id: parse.string(item.id, '记录标识'),
    runtimeId: parse.string(item.runtimeId, '运行标识'),
    generation: parse.number(item.generation, '代次'),
    assetId: parse.number(item.assetId, '资产标识'),
    assetName: parse.string(item.assetName, '资产名称'),
    networkKey: parse.string(item.networkKey, '网段'),
    protocol,
    internalPort: parse.number(item.internalPort, '内部端口'),
    publicPort: parse.number(item.publicPort, '公网端口'),
    endpoint: parse.string(item.endpoint, '访问地址'),
    status: parse.string(item.status, '状态'),
    lastError: item.lastError == null ? null : parse.string(item.lastError, '失败原因'),
    createdAt: parse.number(item.createdAt, '创建时间'),
    revokedAt: item.revokedAt == null ? null : parse.number(item.revokedAt, '撤销时间'),
  }
}

export const teamLabServiceAccessKeys = {
  list: (runtimeId: string) => ['vnext:admin:teamlab:service-access', runtimeId] as const,
}

export const teamLabServiceAccessApi = {
  async list(runtimeId: string) {
    return parse.array(
      await runtimeJsonClient.get(`${root}/${runtimeId}/service-access`),
      '服务开放记录',
      parseServiceAccess
    )
  },
  async create(runtimeId: string, assetId: number, request: CreateTeamLabServiceAccess) {
    return parseServiceAccess(await runtimeJsonClient.postJson(
      `${root}/${runtimeId}/assets/${assetId}/service-access`, request
    ))
  },
  async remove(runtimeId: string, accessId: string) {
    if (!runtimeJsonClient.deleteJson) throw new Error('当前客户端不支持读取撤销结果。')
    return parseServiceAccess(await runtimeJsonClient.deleteJson(`${root}/${runtimeId}/service-access/${accessId}`))
  },
}
