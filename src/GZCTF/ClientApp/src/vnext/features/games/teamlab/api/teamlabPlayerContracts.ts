export type TeamLabPlayerRuntimeStatus =
  | 'pending'
  | 'planning'
  | 'scheduled'
  | 'deploying'
  | 'probing'
  | 'running'
  | 'failed'
  | 'cleanup-pending'
  | 'stopped'
  | 'destroying'
  | 'destroyed'

export interface TeamLabPlayerObjective {
  id: number
  key: string
  assetKey: string
  title: string
  description: string | null
  category: string
  score: number
  solved: boolean
  attempts: number
  maxAttempts: number
  checkpoint: boolean
  prerequisiteKeys: readonly string[]
}

export interface TeamLabPlayerWorkspace {
  gameId: number
  teamId: number
  teamName: string
  runtimeId: string
  status: TeamLabPlayerRuntimeStatus
  stage: string
  resetCount: number
  maxResetCount: number
  objectives: readonly TeamLabPlayerObjective[]
}

export interface TeamLabPlayerAccessGrant {
  id: string
  type: string
  clientAddress: string
  endpoint: string
  allowedIps: string
  dns: string
  createdAt: number
  expiresAt: number | null
  configurationDownloadUrl: string | null
}

export interface TeamLabPlayerResetReceipt {
  runtimeId: string
}

export interface TeamLabPlayerSubmissionResult {
  accepted: boolean
  score: number
  message: string
}

export interface TeamLabPlayerObjectiveProjection extends TeamLabPlayerObjective {
  available: boolean
  remainingAttempts: number | null
}

export interface TeamLabPlayerTargetProjection {
  assetKey: string
  solvedCount: number
  objectiveCount: number
  totalScore: number
  objectives: readonly TeamLabPlayerObjectiveProjection[]
}

export interface TeamLabPlayerWorkspaceProjection {
  gameId: number
  teamId: number
  teamName: string
  runtimeId: string
  status: TeamLabPlayerRuntimeStatus
  stage: string
  resetAllowance: {
    used: number
    limit: number
    remaining: number
  }
  solvedCount: number
  objectiveCount: number
  totalScore: number
  objectives: readonly TeamLabPlayerObjectiveProjection[]
  targets: readonly TeamLabPlayerTargetProjection[]
}
