import {
  AwdpAttackLogItem,
  AwdpChallengeStatus,
  AwdpGameStatusModel,
  AwdpPatchStatus,
  AwdpPatchStatusItem,
  AwdpPatchSubmissionViewModel,
  AwdpRoundStatus,
  AwdpScoreboardItem,
  AwdpServiceStatusModel,
  AwdpServiceViewModel,
  AwdpTeamServiceStatus,
  CheckerStatus,
} from '@Api'
import { externalEntryHref } from '../../shared/urls'

export type AwdpTone = 'danger' | 'info' | 'neutral' | 'success' | 'warning'
export type AwdpPhase = 'attack' | 'finished' | 'patch' | 'unknown'

export interface AwdpStatus {
  gameId: number
  currentRound: number
  roundStartTime: number | null
  attackPhaseMinutes: number
  patchPhaseMinutes: number
  status: AwdpRoundStatus | null
}

export interface AwdpInstance {
  instanceId: number
  serviceId: number
  serviceName: string
  teamId: number
  teamName: string
  ipAddress: string | null
  port: number | null
  endpoint: string | null
  checkerStatus: CheckerStatus | null
  running: boolean
  remainingResetCount: number
  remainingRecoveryCount: number
  canManage: boolean
}

export interface AwdpScore {
  rank: number
  teamId: number
  teamName: string
  ctfScore: number
  awdpScore: number
  totalScore: number
  attackScore: number
  slaScore: number
  patchScore: number
  penaltyScore: number
}

export interface AwdpAttackLog {
  key: string
  time: number | null
  attackerTeam: string
  victimTeam: string
  serviceName: string
  points: number
}

export interface AwdpPatchState {
  serviceId: number
  serviceName: string
  attackStatus: AwdpChallengeStatus | null
  defenseStatus: AwdpChallengeStatus | null
  lastPatchResult: AwdpPatchStatus | null
  lastPatchTime: number | null
  message: string | null
}

export interface AwdpPatchSubmission {
  id: number
  roundNumber: number
  serviceId: number
  serviceName: string
  teamId: number
  teamName: string
  submittedAt: number | null
  checkerResult: CheckerStatus | null
  expResult: AwdpPatchStatus | null
  finalStatus: AwdpPatchStatus | null
  message: string | null
}

export interface AwdpService {
  id: number
  name: string
  imageName: string
  exposePort: number
  checkerScript: string
  checkerEntrypoint: string
  expScript: string
  expEntrypoint: string
  originalScore: number
  attackPoints: number
  slaPoints: number
  patchPoints: number
  serviceAbnormalPenalty: number
  maxAttackPerRound: number
  attackPhaseMinutes: number
  patchPhaseMinutes: number
  totalRounds: number
  maxResetCount: number
  maxRecoveryCount: number
}

export interface AwdpPlayerSnapshot {
  status: AwdpStatus
  instances: AwdpInstance[]
  scoreboard: AwdpScore[]
  attackLogs: AwdpAttackLog[]
  patchStatus: AwdpPatchState[]
}

export interface AwdpAdminSnapshot extends Omit<AwdpPlayerSnapshot, 'patchStatus'> {
  services: AwdpService[]
  patches: AwdpPatchSubmission[]
}

const numberOrZero = (value?: number | null) => (Number.isFinite(value) ? Number(value) : 0)
const stringOrFallback = (value: string | null | undefined, fallback: string) => value?.trim() || fallback

export function normalizeAwdpStatus(value?: AwdpGameStatusModel | null): AwdpStatus {
  return {
    gameId: numberOrZero(value?.gameId),
    currentRound: numberOrZero(value?.currentRound),
    roundStartTime: value?.roundStartTime ? Number(value.roundStartTime) : null,
    attackPhaseMinutes: numberOrZero(value?.attackPhaseMinutes),
    patchPhaseMinutes: numberOrZero(value?.patchPhaseMinutes),
    status: value?.status ?? null,
  }
}

export function normalizeAwdpInstance(value: AwdpTeamServiceStatus): AwdpInstance {
  const ipAddress = value.ipAddress?.trim() || null
  const port = value.port ?? null
  const endpoint = ipAddress ? externalEntryHref(`${ipAddress}${port ? `:${port}` : ''}`) : null
  return {
    instanceId: numberOrZero(value.instanceId),
    serviceId: numberOrZero(value.serviceId),
    serviceName: stringOrFallback(value.serviceName, '未命名服务'),
    teamId: numberOrZero(value.teamId),
    teamName: stringOrFallback(value.teamName, '未命名战队'),
    ipAddress,
    port,
    endpoint,
    checkerStatus: value.lastCheckerStatus ?? null,
    running: Boolean(value.isRunning),
    remainingResetCount: numberOrZero(value.remainingResetCount),
    remainingRecoveryCount: numberOrZero(value.remainingRecoveryCount),
    canManage: Boolean(value.canManage),
  }
}

export function flattenAwdpInstances(values: AwdpServiceStatusModel[]) {
  return values.flatMap((service) => service.teamStatuses ?? []).map(normalizeAwdpInstance)
}

export function normalizeAwdpScore(value: AwdpScoreboardItem): AwdpScore {
  return {
    rank: numberOrZero(value.rank),
    teamId: numberOrZero(value.teamId),
    teamName: stringOrFallback(value.teamName, '未命名战队'),
    ctfScore: numberOrZero(value.ctfScore),
    awdpScore: numberOrZero(value.awdpScore),
    totalScore: numberOrZero(value.totalScore),
    attackScore: numberOrZero(value.attackScore),
    slaScore: numberOrZero(value.slaScore),
    patchScore: numberOrZero(value.patchScore),
    penaltyScore: numberOrZero(value.penaltyScore),
  }
}

export function normalizeAwdpAttackLog(value: AwdpAttackLogItem, index: number): AwdpAttackLog {
  const time = value.time ? Number(value.time) : null
  const attackerTeam = stringOrFallback(value.attackerTeam, '未知攻击方')
  const victimTeam = stringOrFallback(value.victimTeam, '未知目标')
  const serviceName = stringOrFallback(value.serviceName, '未知服务')
  return {
    key: `${time ?? 0}:${attackerTeam}:${victimTeam}:${serviceName}:${index}`,
    time,
    attackerTeam,
    victimTeam,
    serviceName,
    points: numberOrZero(value.points),
  }
}

export function normalizeAwdpPatchState(value: AwdpPatchStatusItem): AwdpPatchState {
  return {
    serviceId: numberOrZero(value.serviceId),
    serviceName: stringOrFallback(value.serviceName, '未命名服务'),
    attackStatus: value.attackStatus ?? null,
    defenseStatus: value.defenseStatus ?? null,
    lastPatchResult: value.lastPatchResult ?? null,
    lastPatchTime: value.lastPatchTime ? Number(value.lastPatchTime) : null,
    message: value.message?.trim() || null,
  }
}

export function normalizeAwdpPatchSubmission(value: AwdpPatchSubmissionViewModel): AwdpPatchSubmission {
  return {
    id: numberOrZero(value.id),
    roundNumber: numberOrZero(value.roundNumber),
    serviceId: numberOrZero(value.serviceId),
    serviceName: stringOrFallback(value.serviceName, '未命名服务'),
    teamId: numberOrZero(value.teamId),
    teamName: stringOrFallback(value.teamName, '未命名战队'),
    submittedAt: value.submittedAt ? Number(value.submittedAt) : null,
    checkerResult: value.checkerResult ?? null,
    expResult: value.expResult ?? null,
    finalStatus: value.finalStatus ?? null,
    message: value.message?.trim() || null,
  }
}

export function normalizeAwdpService(value: AwdpServiceViewModel): AwdpService {
  return {
    id: numberOrZero(value.id),
    name: stringOrFallback(value.name, '未命名服务'),
    imageName: value.imageName?.trim() || '',
    exposePort: numberOrZero(value.exposePort),
    checkerScript: value.checkerScript ?? '',
    checkerEntrypoint: value.checkerEntrypoint ?? '',
    expScript: value.expScript ?? '',
    expEntrypoint: value.expEntrypoint ?? '',
    originalScore: numberOrZero(value.originalScore),
    attackPoints: numberOrZero(value.attackPoints),
    slaPoints: numberOrZero(value.slaPoints),
    patchPoints: numberOrZero(value.patchPoints),
    serviceAbnormalPenalty: numberOrZero(value.serviceAbnormalPenalty),
    maxAttackPerRound: numberOrZero(value.maxAttackPerRound),
    attackPhaseMinutes: numberOrZero(value.attackPhaseMinutes),
    patchPhaseMinutes: numberOrZero(value.patchPhaseMinutes),
    totalRounds: numberOrZero(value.totalRounds),
    maxResetCount: numberOrZero(value.maxResetCount),
    maxRecoveryCount: numberOrZero(value.maxRecoveryCount),
  }
}

export function awdpPhase(status: AwdpRoundStatus | null): AwdpPhase {
  if (status === AwdpRoundStatus.AttackPhase) return 'attack'
  if (status === AwdpRoundStatus.PatchPhase) return 'patch'
  if (status === AwdpRoundStatus.Finished) return 'finished'
  return 'unknown'
}

export function phaseMeta(status: AwdpRoundStatus | null): { label: string; tone: AwdpTone } {
  const phase = awdpPhase(status)
  if (phase === 'attack') return { label: '攻击阶段', tone: 'info' }
  if (phase === 'patch') return { label: '修补阶段', tone: 'warning' }
  if (phase === 'finished') return { label: '已结束', tone: 'neutral' }
  return { label: '尚未开始', tone: 'neutral' }
}

export function checkerMeta(status: CheckerStatus | null): { label: string; tone: AwdpTone } {
  if (status === CheckerStatus.OK) return { label: '正常', tone: 'success' }
  if (status === CheckerStatus.Mumble) return { label: '部分异常', tone: 'warning' }
  if (status === CheckerStatus.Down) return { label: '不可用', tone: 'danger' }
  if (status === CheckerStatus.Corrupt) return { label: '数据异常', tone: 'danger' }
  if (status === CheckerStatus.Skipped) return { label: '未检测', tone: 'neutral' }
  return { label: '等待检测', tone: 'neutral' }
}

export function patchMeta(status: AwdpPatchStatus | null): { label: string; tone: AwdpTone } {
  if (status === AwdpPatchStatus.ExpFailed) return { label: '漏洞已阻断', tone: 'success' }
  if (status === AwdpPatchStatus.ExpSucceeded) return { label: '漏洞仍可利用', tone: 'danger' }
  if (status === AwdpPatchStatus.CheckerFailed) return { label: '服务校验失败', tone: 'danger' }
  if (status === AwdpPatchStatus.Timeout) return { label: '验证超时', tone: 'warning' }
  if (status === AwdpPatchStatus.Unsupported) return { label: '不支持验证', tone: 'warning' }
  if (status === AwdpPatchStatus.Pending) return { label: '验证中', tone: 'info' }
  return { label: '未提交', tone: 'neutral' }
}

export function defenseMeta(status: AwdpChallengeStatus | null): { label: string; tone: AwdpTone } {
  if (status === AwdpChallengeStatus.Defended) return { label: '已防守', tone: 'success' }
  if (status === AwdpChallengeStatus.DefenseAbnormal) return { label: '服务异常', tone: 'danger' }
  if (status === AwdpChallengeStatus.DefenseFailed) return { label: '防守失败', tone: 'danger' }
  return { label: '未防守', tone: 'neutral' }
}

export function phaseEndsAt(status: AwdpStatus) {
  if (!status.roundStartTime) return null
  const phase = awdpPhase(status.status)
  if (phase === 'attack') return status.roundStartTime + status.attackPhaseMinutes * 60_000
  if (phase === 'patch') return status.roundStartTime + (status.attackPhaseMinutes + status.patchPhaseMinutes) * 60_000
  return null
}

export function remainingPhaseLabel(status: AwdpStatus, now: number) {
  const end = phaseEndsAt(status)
  if (!end) return '--:--'
  const seconds = Math.max(0, Math.floor((end - now) / 1000))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  return hours
    ? `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
    : `${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}

export function formatAwdpTime(value: number | null) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(value)
}

export function resolveMyTeamId(instances: AwdpInstance[], scoreboard: AwdpScore[], teamName?: string | null) {
  const manageable = instances.find((item) => item.canManage && item.teamId > 0)
  if (manageable) return manageable.teamId
  const normalizedName = teamName?.trim()
  if (normalizedName) return scoreboard.find((item) => item.teamName === normalizedName)?.teamId ?? null
  return scoreboard.length === 1 ? scoreboard[0].teamId : null
}
