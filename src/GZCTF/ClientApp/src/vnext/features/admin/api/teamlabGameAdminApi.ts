import { teamLabContractFailure } from '../teamlab/api/teamlabErrors'
import { teamLabParsing as parse } from '../teamlab/api/teamlabParsers'
import type { TeamLabRuntimeStatus } from '../teamlab/api/teamlabContracts'
import { runtimeJsonClient, type RuntimeJsonClient } from './runtimeJsonClient'

export type TeamLabGameRolloutStatus =
  | 'draft'
  | 'preparing'
  | 'rollingout'
  | 'ready'
  | 'draining'
  | 'completed'
  | 'blocked'
  | 'failed'

export type TeamLabGameTargetStatus =
  | 'pending'
  | 'provisioning'
  | 'ready'
  | 'accessopen'
  | 'failed'
  | 'draining'
  | 'cleanuppending'
  | 'destroyed'

export interface TeamLabGameObjective {
  id: number
  key: string
  assetKey: string
  title: string
  description: string | null
  category: string
  score: number
  dynamic: boolean
  maxAttempts: number
  visible: boolean
  checkpoint: boolean
  prerequisiteKeys: readonly string[]
  orderIndex: number
}

export interface TeamLabGameObjectiveWrite {
  id?: number
  key: string
  assetKey: string
  title: string
  description: string | null
  category: string
  score: number
  dynamic: boolean
  staticFlag?: string | null
  flagTemplate?: string | null
  maxAttempts: number
  visible: boolean
  checkpoint: boolean
  prerequisiteKeys: readonly string[]
  orderIndex: number
}

export interface ReplaceTeamLabGameObjectivesRequest {
  revision: number
  maxResetCount: number
  objectives: readonly TeamLabGameObjectiveWrite[]
}

export interface TeamLabGameBinding {
  gameId: number
  topologyId: string
  activeReleaseId: string | null
  maxResetCount: number
  objectiveRevision: number
  objectives: readonly TeamLabGameObjective[]
}

export interface TeamLabGameReleaseOption {
  topologyId: string
  topologyName: string
  releaseId: string
  version: number
  networkCount: number
  assetCount: number
  publishedAt: number
}

export interface TeamLabGameRolloutCounts {
  total: number
  pending: number
  provisioning: number
  ready: number
  accessOpen: number
  failed: number
  draining: number
  destroyed: number
}

export interface TeamLabGameRollout {
  id: string
  releaseId: string
  status: TeamLabGameRolloutStatus
  preparationRequested: boolean
  desiredAccessOpen: boolean
  drainRequested: boolean
  counts: TeamLabGameRolloutCounts
  preparedAt: number | null
  accessOpenedAt: number | null
  drainingAt: number | null
  completedAt: number | null
  createdAt: number
  updatedAt: number
  error: string | null
}

export interface TeamLabGameState {
  binding: TeamLabGameBinding | null
  rollout: TeamLabGameRollout | null
}

export interface TeamLabGameTarget {
  id: string
  externalSubject: string
  teamId: number
  displayName: string
  runtimeId: string | null
  status: TeamLabGameTargetStatus
  operationId: string | null
  runtimeStatus: TeamLabRuntimeStatus | null
  runtimeStage: string | null
  createdAt: number
  updatedAt: number
  error: string | null
}

export interface TeamLabGameTargetPage {
  items: readonly TeamLabGameTarget[]
  nextCursor: string | null
}

export interface TeamLabOperatorGrant {
  userId: string
  userName: string
  displayName: string | null
  viewAssets: boolean
  operateAssets: boolean
  updatedAt: number
}

export interface TeamLabOperatorGrantWrite {
  viewAssets: boolean
  operateAssets: boolean
}

export interface TeamLabTeamRebuildResult {
  runtimeId: string
  reused: boolean
}

const rolloutStatuses = {
  draft: 'draft', preparing: 'preparing', rollingout: 'rollingout', ready: 'ready',
  draining: 'draining', completed: 'completed', blocked: 'blocked', failed: 'failed',
} as const
const targetStatuses = {
  pending: 'pending', provisioning: 'provisioning', ready: 'ready', accessopen: 'accessopen',
  failed: 'failed', draining: 'draining', cleanuppending: 'cleanuppending', destroyed: 'destroyed',
} as const
const runtimeStatuses = {
  0: 'pending', 1: 'planning', 2: 'scheduled', 3: 'deploying', 4: 'probing', 5: 'running',
  6: 'failed', 7: 'cleanup-pending', 8: 'stopped', 9: 'destroying', 10: 'destroyed',
  Pending: 'pending', Planning: 'planning', Scheduled: 'scheduled', Deploying: 'deploying',
  Probing: 'probing', Running: 'running', Failed: 'failed', CleanupPending: 'cleanup-pending',
  Stopped: 'stopped', Destroying: 'destroying', Destroyed: 'destroyed',
} as const

function nullable<T>(value: unknown, label: string, parser: (input: unknown, inputLabel: string) => T): T | null {
  return value === null || value === undefined ? null : parser(value, label)
}

function parseObjective(value: unknown, label: string): TeamLabGameObjective {
  const item = parse.record(value, label)
  return {
    id: parse.number(item.id, `${label}.id`),
    key: parse.string(item.key, `${label}.key`),
    assetKey: parse.string(item.assetKey, `${label}.assetKey`),
    title: parse.string(item.title, `${label}.title`),
    description: parse.nullableString(item.description, `${label}.description`),
    category: parse.string(item.category, `${label}.category`),
    score: parse.number(item.score, `${label}.score`),
    dynamic: parse.boolean(item.dynamic, `${label}.dynamic`),
    maxAttempts: parse.number(item.maxAttempts, `${label}.maxAttempts`),
    visible: parse.boolean(item.visible, `${label}.visible`),
    checkpoint: parse.boolean(item.checkpoint, `${label}.checkpoint`),
    prerequisiteKeys: parse.array(item.prerequisiteKeys, `${label}.prerequisiteKeys`, parse.string),
    orderIndex: parse.number(item.orderIndex, `${label}.orderIndex`),
  }
}

export function parseTeamLabGameBinding(value: unknown): TeamLabGameBinding {
  const item = parse.record(value, 'TeamLab game binding')
  return {
    gameId: parse.number(item.gameId, 'TeamLab game binding.gameId'),
    topologyId: parse.string(item.topologyId, 'TeamLab game binding.topologyId'),
    activeReleaseId: parse.nullableString(item.activeReleaseId, 'TeamLab game binding.activeReleaseId'),
    maxResetCount: parse.number(item.maxResetCount, 'TeamLab game binding.maxResetCount'),
    objectiveRevision: parse.number(item.objectiveRevision, 'TeamLab game binding.objectiveRevision'),
    objectives: parse.array(item.objectives, 'TeamLab game binding.objectives', parseObjective),
  }
}

function parseCounts(value: unknown): TeamLabGameRolloutCounts {
  const item = parse.record(value, 'TeamLab rollout counts')
  return {
    total: parse.number(item.total, 'TeamLab rollout counts.total'),
    pending: parse.number(item.pending, 'TeamLab rollout counts.pending'),
    provisioning: parse.number(item.provisioning, 'TeamLab rollout counts.provisioning'),
    ready: parse.number(item.ready, 'TeamLab rollout counts.ready'),
    accessOpen: parse.number(item.accessOpen, 'TeamLab rollout counts.accessOpen'),
    failed: parse.number(item.failed, 'TeamLab rollout counts.failed'),
    draining: parse.number(item.draining, 'TeamLab rollout counts.draining'),
    destroyed: parse.number(item.destroyed, 'TeamLab rollout counts.destroyed'),
  }
}

export function parseTeamLabGameRollout(value: unknown): TeamLabGameRollout {
  const item = parse.record(value, 'TeamLab game rollout')
  return {
    id: parse.string(item.id, 'TeamLab game rollout.id'),
    releaseId: parse.string(item.releaseId, 'TeamLab game rollout.releaseId'),
    status: parse.enumValue(item.status, rolloutStatuses, 'TeamLab game rollout.status'),
    preparationRequested: parse.boolean(item.preparationRequested, 'TeamLab game rollout.preparationRequested'),
    desiredAccessOpen: parse.boolean(item.desiredAccessOpen, 'TeamLab game rollout.desiredAccessOpen'),
    drainRequested: parse.boolean(item.drainRequested, 'TeamLab game rollout.drainRequested'),
    counts: parseCounts(item.counts),
    preparedAt: parse.nullableNumber(item.preparedAt, 'TeamLab game rollout.preparedAt'),
    accessOpenedAt: parse.nullableNumber(item.accessOpenedAt, 'TeamLab game rollout.accessOpenedAt'),
    drainingAt: parse.nullableNumber(item.drainingAt, 'TeamLab game rollout.drainingAt'),
    completedAt: parse.nullableNumber(item.completedAt, 'TeamLab game rollout.completedAt'),
    createdAt: parse.number(item.createdAt, 'TeamLab game rollout.createdAt'),
    updatedAt: parse.number(item.updatedAt, 'TeamLab game rollout.updatedAt'),
    error: parse.nullableString(item.error, 'TeamLab game rollout.error'),
  }
}

export function parseTeamLabGameState(value: unknown): TeamLabGameState {
  const item = parse.record(value, 'TeamLab game state')
  return {
    binding: nullable(item.binding, 'TeamLab game state.binding', parseTeamLabGameBinding),
    rollout: nullable(item.rollout, 'TeamLab game state.rollout', parseTeamLabGameRollout),
  }
}

export function parseTeamLabGameReleaseOptions(value: unknown): readonly TeamLabGameReleaseOption[] {
  return parse.array(value, 'TeamLab game releases', (entry, label) => {
    const item = parse.record(entry, label)
    return {
      topologyId: parse.string(item.topologyId, `${label}.topologyId`),
      topologyName: parse.string(item.topologyName, `${label}.topologyName`),
      releaseId: parse.string(item.releaseId, `${label}.releaseId`),
      version: parse.number(item.version, `${label}.version`),
      networkCount: parse.number(item.networkCount, `${label}.networkCount`),
      assetCount: parse.number(item.assetCount, `${label}.assetCount`),
      publishedAt: parse.number(item.publishedAt, `${label}.publishedAt`),
    }
  })
}

function teamIdFromSubject(value: string, label: string) {
  const match = /^team:([1-9]\d*)$/.exec(value)
  if (!match) return teamLabContractFailure(label, value)
  return Number(match[1])
}

export function parseTeamLabGameTargetPage(value: unknown): TeamLabGameTargetPage {
  const page = parse.record(value, 'TeamLab rollout target page')
  return {
    items: parse.array(page.items, 'TeamLab rollout target page.items', (entry, label) => {
      const item = parse.record(entry, label)
      const externalSubject = parse.string(item.externalSubject, `${label}.externalSubject`)
      return {
        id: parse.string(item.id, `${label}.id`),
        externalSubject,
        teamId: teamIdFromSubject(externalSubject, `${label}.externalSubject`),
        displayName: parse.string(item.displayName, `${label}.displayName`),
        runtimeId: parse.nullableString(item.runtimeId, `${label}.runtimeId`),
        status: parse.enumValue(item.status, targetStatuses, `${label}.status`),
        operationId: parse.nullableString(item.operationId, `${label}.operationId`),
        runtimeStatus: nullable(item.runtimeStatus, `${label}.runtimeStatus`, (input, inputLabel) =>
          parse.enumValue<TeamLabRuntimeStatus>(input, runtimeStatuses, inputLabel)),
        runtimeStage: parse.nullableString(item.runtimeStage, `${label}.runtimeStage`),
        createdAt: parse.number(item.createdAt, `${label}.createdAt`),
        updatedAt: parse.number(item.updatedAt, `${label}.updatedAt`),
        error: parse.nullableString(item.error, `${label}.error`),
      }
    }),
    nextCursor: parse.nullableString(page.nextCursor, 'TeamLab rollout target page.nextCursor'),
  }
}

export function parseTeamLabOperatorGrants(value: unknown): readonly TeamLabOperatorGrant[] {
  return parse.array(value, 'TeamLab operator grants', (entry, label) => {
    const item = parse.record(entry, label)
    return {
      userId: parse.string(item.userId, `${label}.userId`),
      userName: parse.string(item.userName, `${label}.userName`),
      displayName: parse.nullableString(item.displayName, `${label}.displayName`),
      viewAssets: parse.boolean(item.viewAssets, `${label}.viewAssets`),
      operateAssets: parse.boolean(item.operateAssets, `${label}.operateAssets`),
      updatedAt: parse.number(item.updatedAt, `${label}.updatedAt`),
    }
  })
}

function parseRebuild(value: unknown): TeamLabTeamRebuildResult {
  const item = parse.record(value, 'TeamLab team rebuild')
  return {
    runtimeId: parse.string(item.runtimeId, 'TeamLab team rebuild.runtimeId'),
    reused: parse.boolean(item.reused, 'TeamLab team rebuild.reused'),
  }
}

export const teamLabGameAdminKeys = {
  state: (gameId: number) => ['vnext:admin:game-teamlab', gameId] as const,
  releases: (gameId: number) => ['vnext:admin:game-teamlab-releases', gameId] as const,
  targets: (gameId: number, cursor: string | null, limit: number) =>
    ['vnext:admin:game-teamlab-targets', gameId, cursor ?? '', limit] as const,
  operators: (gameId: number) => ['vnext:admin:game-teamlab-operators', gameId] as const,
}

export function createTeamLabGameAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  const root = (gameId: number) => `/api/admin/pentest/games/${gameId}`
  return {
    async state(gameId: number) {
      return parseTeamLabGameState(await client.get(`${root(gameId)}/teamlab`))
    },
    async releases(gameId: number) {
      return parseTeamLabGameReleaseOptions(await client.get(`${root(gameId)}/teamlab/releases`))
    },
    async bind(gameId: number, topologyId: string) {
      return parseTeamLabGameBinding(await client.putJson(`${root(gameId)}/binding`, { topologyId }))
    },
    async activateRelease(gameId: number, releaseId: string) {
      return parseTeamLabGameBinding(await client.postJson(`${root(gameId)}/releases/${releaseId}/activate`))
    },
    async replaceObjectives(gameId: number, request: ReplaceTeamLabGameObjectivesRequest) {
      return parseTeamLabGameBinding(await client.putJson(`${root(gameId)}/objectives`, request))
    },
    async prepare(gameId: number) {
      return parseTeamLabGameRollout(await client.postJson(`${root(gameId)}/teamlab/prepare`))
    },
    async setAccess(gameId: number, open: boolean) {
      return parseTeamLabGameRollout(await client.postJson(`${root(gameId)}/teamlab/access/${open ? 'open' : 'close'}`))
    },
    async drain(gameId: number) {
      return parseTeamLabGameRollout(await client.postJson(`${root(gameId)}/teamlab/drain`))
    },
    async targets(gameId: number, after?: string, limit = 30) {
      return parseTeamLabGameTargetPage(await client.get(`${root(gameId)}/teamlab/targets`, { after, limit }))
    },
    async rebuildTeam(gameId: number, teamId: number) {
      return parseRebuild(await client.postJson(`${root(gameId)}/teams/${teamId}/rebuild`))
    },
    async cleanupTeam(gameId: number, teamId: number) {
      await client.postJson(`${root(gameId)}/teams/${teamId}/cleanup`)
    },
    async operators(gameId: number) {
      return parseTeamLabOperatorGrants(await client.get(`${root(gameId)}/teamlab/operators`))
    },
    async setOperator(gameId: number, userId: string, request: TeamLabOperatorGrantWrite) {
      await client.putJson(`${root(gameId)}/teamlab/operators/${userId}`, request)
    },
    async deleteOperator(gameId: number, userId: string) {
      await client.delete(`${root(gameId)}/teamlab/operators/${userId}`)
    },
  }
}

export const teamLabGameAdminApi = createTeamLabGameAdminApi()
