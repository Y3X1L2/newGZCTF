import type {
  TeamLabPlayerAccessGrant,
  TeamLabPlayerObjective,
  TeamLabPlayerResetReceipt,
  TeamLabPlayerRuntimeStatus,
  TeamLabPlayerSubmissionResult,
  TeamLabPlayerWorkspace,
} from './teamlabPlayerContracts'

type UnknownRecord = Record<string, unknown>

const runtimeStatuses: Readonly<Record<string, TeamLabPlayerRuntimeStatus>> = {
  0: 'pending',
  1: 'planning',
  2: 'scheduled',
  3: 'deploying',
  4: 'probing',
  5: 'running',
  6: 'failed',
  7: 'cleanup-pending',
  8: 'stopped',
  9: 'destroying',
  10: 'destroyed',
  Pending: 'pending',
  Planning: 'planning',
  Scheduled: 'scheduled',
  Deploying: 'deploying',
  Probing: 'probing',
  Running: 'running',
  Failed: 'failed',
  CleanupPending: 'cleanup-pending',
  Stopped: 'stopped',
  Destroying: 'destroying',
  Destroyed: 'destroyed',
}

export class TeamLabPlayerContractError extends Error {
  readonly field: string

  constructor(field: string) {
    super(`Invalid TeamLab player response at ${field}.`)
    this.name = 'TeamLabPlayerContractError'
    this.field = field
  }
}

function failure(field: string): never {
  throw new TeamLabPlayerContractError(field)
}

function record(value: unknown, field: string): UnknownRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return failure(field)
  return value as UnknownRecord
}

function string(value: unknown, field: string): string {
  if (typeof value !== 'string') return failure(field)
  return value
}

function nullableString(value: unknown, field: string): string | null {
  return value === null ? null : string(value, field)
}

function boolean(value: unknown, field: string): boolean {
  if (typeof value !== 'boolean') return failure(field)
  return value
}

function integer(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value)) return failure(field)
  return value
}

function nonNegativeInteger(value: unknown, field: string): number {
  const parsed = integer(value, field)
  if (parsed < 0) return failure(field)
  return parsed
}

function positiveInteger(value: unknown, field: string): number {
  const parsed = integer(value, field)
  if (parsed <= 0) return failure(field)
  return parsed
}

function timestamp(value: unknown, field: string): number {
  const parsed = nonNegativeInteger(value, field)
  return parsed
}

function nullableTimestamp(value: unknown, field: string): number | null {
  return value === null ? null : timestamp(value, field)
}

function guid(value: unknown, field: string): string {
  const parsed = string(value, field)
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(parsed)) {
    return failure(field)
  }
  return parsed
}

function stringArray(value: unknown, field: string): string[] {
  if (!Array.isArray(value)) return failure(field)
  return value.map((item, index) => string(item, `${field}[${index}]`))
}

function runtimeStatus(value: unknown, field: string): TeamLabPlayerRuntimeStatus {
  const parsed = runtimeStatuses[String(value)]
  if (!parsed) return failure(field)
  return parsed
}

function objective(value: unknown, field: string): TeamLabPlayerObjective {
  const item = record(value, field)
  return {
    id: positiveInteger(item.id, `${field}.id`),
    key: string(item.key, `${field}.key`),
    assetKey: string(item.assetKey, `${field}.assetKey`),
    title: string(item.title, `${field}.title`),
    description: nullableString(item.description, `${field}.description`),
    category: string(item.category, `${field}.category`),
    score: nonNegativeInteger(item.score, `${field}.score`),
    solved: boolean(item.solved, `${field}.solved`),
    attempts: nonNegativeInteger(item.attempts, `${field}.attempts`),
    maxAttempts: nonNegativeInteger(item.maxAttempts, `${field}.maxAttempts`),
    checkpoint: boolean(item.checkpoint, `${field}.checkpoint`),
    prerequisiteKeys: stringArray(item.prerequisiteKeys, `${field}.prerequisiteKeys`),
  }
}

export function parseTeamLabPlayerWorkspace(value: unknown): TeamLabPlayerWorkspace {
  const item = record(value, 'workspace')
  if (!Array.isArray(item.objectives)) return failure('workspace.objectives')
  return {
    gameId: positiveInteger(item.gameId, 'workspace.gameId'),
    teamId: positiveInteger(item.teamId, 'workspace.teamId'),
    teamName: string(item.teamName, 'workspace.teamName'),
    runtimeId: guid(item.runtimeId, 'workspace.runtimeId'),
    status: runtimeStatus(item.status, 'workspace.status'),
    stage: string(item.stage, 'workspace.stage'),
    resetCount: nonNegativeInteger(item.resetCount, 'workspace.resetCount'),
    maxResetCount: nonNegativeInteger(item.maxResetCount, 'workspace.maxResetCount'),
    objectives: item.objectives.map((entry, index) => objective(entry, `workspace.objectives[${index}]`)),
  }
}

export function parseTeamLabPlayerAccessGrant(value: unknown): TeamLabPlayerAccessGrant {
  const item = record(value, 'accessGrant')
  return {
    id: guid(item.id, 'accessGrant.id'),
    type: string(item.type, 'accessGrant.type'),
    clientAddress: string(item.clientAddress, 'accessGrant.clientAddress'),
    endpoint: string(item.endpoint, 'accessGrant.endpoint'),
    allowedIps: string(item.allowedIps, 'accessGrant.allowedIps'),
    dns: string(item.dns, 'accessGrant.dns'),
    createdAt: timestamp(item.createdAt, 'accessGrant.createdAt'),
    expiresAt: nullableTimestamp(item.expiresAt, 'accessGrant.expiresAt'),
    configurationDownloadUrl: nullableString(
      item.configurationDownloadUrl,
      'accessGrant.configurationDownloadUrl'
    ),
  }
}

export function parseTeamLabPlayerResetReceipt(value: unknown): TeamLabPlayerResetReceipt {
  const item = record(value, 'resetReceipt')
  return { runtimeId: guid(item.runtimeId, 'resetReceipt.runtimeId') }
}

export function parseTeamLabPlayerSubmissionResult(value: unknown): TeamLabPlayerSubmissionResult {
  const item = record(value, 'submissionResult')
  return {
    accepted: boolean(item.accepted, 'submissionResult.accepted'),
    score: nonNegativeInteger(item.score, 'submissionResult.score'),
    message: string(item.message, 'submissionResult.message'),
  }
}
