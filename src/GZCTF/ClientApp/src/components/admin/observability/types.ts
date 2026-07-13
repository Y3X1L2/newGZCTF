export interface OperationalEventRecord {
  id: number
  occurredAt: string
  correlationId: string
  traceId?: string | null
  eventCode: string
  severity: string | number
  outcome: string | number
  errorCategory?: string | number | null
  errorCode?: string | null
  retryable: boolean
  message: string
  detail?: Record<string, unknown> | null
  actorUserId?: string | null
  ownerUserId?: string | null
  ownerTeamId?: number | null
  gameId?: number | null
  courseId?: number | null
  challengeId?: number | null
  imageTemplateId?: number | null
  workerNodeId?: string | null
  deploymentTicketId?: string | null
  teamLabRuntimeId?: number | null
  vmInstanceId?: string | null
  subjectType?: string | null
  subjectId?: string | null
  subjectDisplayName?: string | null
  resourceType?: string | null
  resourceId?: string | null
  resourceDisplayName?: string | null
}

export interface OperationalEventLabels {
  actor?: string | null
  owner?: string | null
  team?: string | null
  game?: string | null
  course?: string | null
  challenge?: string | null
  imageTemplate?: string | null
  workerNode?: string | null
  deploymentTicket?: string | null
  teamLabRuntime?: string | null
  vmInstance?: string | null
  subject?: string | null
  resource?: string | null
}

export interface OperationalEventItem {
  event: OperationalEventRecord
  domain: string
  labels: OperationalEventLabels
}

export interface OperationalEventPage {
  items: OperationalEventItem[]
  nextCursor?: string | null
}

export interface OperationalCorrelationSummary {
  correlationId: string
  startedAt: string
  completedAt: string
  outcome: string | number
  errorCategory?: string | number | null
  errorCode?: string | null
  eventCount: number
  domains: string[]
  workerNodes: string[]
  subject?: string | null
  resource?: string | null
}

export interface OperationalEventFilters {
  correlationId?: string
  workerNodeId?: string
  imageTemplateId?: string
  deploymentTicketId?: string
}
