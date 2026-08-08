import { runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'

export type TeamLabServiceProfileAssetKind = 'docker' | 'vm'

export interface TeamLabServiceProfileParameter {
  key: string
  type: string
  required: boolean
  secret: boolean
  defaultValue: string | null
}

export interface TeamLabServiceProfileExecution {
  steps: number
  healthChecks: number
  maxReboots: number
  phase: string
}

export interface TeamLabServiceProfileSummary {
  id: string
  version: number
  availableVersions: readonly number[]
  name: string
  description: string | null
  assetKinds: readonly TeamLabServiceProfileAssetKind[]
  updatedAt: number
}

export interface TeamLabServiceProfileDetail extends TeamLabServiceProfileSummary {
  parameters: readonly TeamLabServiceProfileParameter[]
  execution: TeamLabServiceProfileExecution
  status: string
  documentationUrl: string | null
  publishedAt: number
}

export interface TeamLabServiceProfilePage {
  items: readonly TeamLabServiceProfileSummary[]
  nextCursor: string | null
}

const root = '/api/admin/teamlab/service-profiles'
const assetKinds = { 0: 'docker', 1: 'vm', Docker: 'docker', Vm: 'vm' } as const

export const teamLabServiceProfileKeys = {
  list: () => ['vnext:admin:teamlab:service-profiles'] as const,
  detail: (profileId: string, version: number) =>
    ['vnext:admin:teamlab:service-profile', profileId, version] as const,
}

function parseParameter(value: unknown, label: string): TeamLabServiceProfileParameter {
  const item = parse.record(value, label)
  return {
    key: parse.string(item.key, `${label}.key`),
    type: parse.string(item.type, `${label}.type`),
    required: parse.boolean(item.required, `${label}.required`),
    secret: parse.boolean(item.secret, `${label}.secret`),
    defaultValue: parse.nullableString(item.defaultValue, `${label}.defaultValue`),
  }
}

function parseExecution(value: unknown, label: string): TeamLabServiceProfileExecution {
  const item = parse.record(value, label)
  return {
    steps: parse.number(item.steps, `${label}.steps`),
    healthChecks: parse.number(item.healthChecks, `${label}.healthChecks`),
    maxReboots: parse.number(item.maxReboots, `${label}.maxReboots`),
    phase: parse.string(item.phase, `${label}.phase`),
  }
}

function parseSummary(value: unknown, label: string): TeamLabServiceProfileSummary {
  const item = parse.record(value, label)
  return {
    id: parse.string(item.id, `${label}.id`),
    version: parse.number(item.version, `${label}.version`),
    availableVersions: parse.array(item.availableVersions, `${label}.availableVersions`, parse.number).sort((left, right) => right - left),
    name: parse.string(item.name, `${label}.name`),
    description: parse.nullableString(item.description, `${label}.description`),
    assetKinds: parse.array(item.assetKinds, `${label}.assetKinds`, (entry, entryLabel) =>
      parse.enumValue<TeamLabServiceProfileAssetKind>(entry, assetKinds, entryLabel)
    ),
    updatedAt: parse.number(item.updatedAt, `${label}.updatedAt`),
  }
}

function parseDetail(value: unknown): TeamLabServiceProfileDetail {
  const item = parse.record(value, '服务目录详情')
  return {
    ...parseSummary(item, '服务目录详情'),
    parameters: parse.array(item.parameters, '服务目录详情.parameters', parseParameter),
    execution: parseExecution(item.execution, '服务目录详情.execution'),
    status: parse.string(item.status, '服务目录详情.status'),
    documentationUrl: parse.nullableString(item.documentationUrl, '服务目录详情.documentationUrl'),
    publishedAt: parse.number(item.publishedAt, '服务目录详情.publishedAt'),
  }
}

export function createTeamLabServiceProfileApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async list(limit = 100, after?: string): Promise<TeamLabServiceProfilePage> {
      const page = parse.record(await client.get(root, { limit, after }), '服务目录分页')
      return {
        items: parse.array(page.items, '服务目录分页.items', parseSummary),
        nextCursor: parse.nullableString(page.nextCursor, '服务目录分页.nextCursor'),
      }
    },
    async detail(profileId: string, version: number): Promise<TeamLabServiceProfileDetail> {
      return parseDetail(await client.get(`${root}/${profileId}`, { version }))
    },
  }
}

export const teamLabServiceProfileApi = createTeamLabServiceProfileApi()
