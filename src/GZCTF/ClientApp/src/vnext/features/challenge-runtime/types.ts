import { AnswerResult, ContainerEntryStatus } from '@Api'

export type RuntimeInstancePhase = 'idle' | 'queued' | 'provisioning' | 'running' | 'extending' | 'stopping' | 'failed'

export interface RuntimeQueueStatus {
  queuePosition?: number | null
  peopleAhead?: number | null
  targetNodeName?: string | null
}

export interface RuntimeVmStatus {
  stageMessage?: string | null
  queue?: RuntimeQueueStatus | null
}

export interface RuntimeInstanceController {
  kind: 'none' | 'docker' | 'windows'
  phase: RuntimeInstancePhase
  entry: string | null
  entryStatus: ContainerEntryStatus | null
  entryReadyAt: number | null
  entryError: string | null
  closeTime: number | null
  vmStatus: RuntimeVmStatus | null
  error: string | null
  busy: boolean
  create: () => Promise<void>
  extend: () => Promise<void>
  destroy: () => Promise<void>
  refresh: () => Promise<void>
}

export interface ChallengeFeedback {
  tone: 'success' | 'danger' | 'neutral'
  message: string
  result: AnswerResult | 'Error'
}

export interface FlagStep {
  id?: number
  orderIndex?: number | null
  description?: string | null
}

export interface FlagChallengeState {
  attempts?: number | null
  flags?: FlagStep[] | null
  limit?: number | null
}
