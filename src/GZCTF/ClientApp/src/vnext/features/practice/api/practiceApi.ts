import useSWR from 'swr'
import { ContainerEntryStatus } from '@Api'

const BASE = '/api/exercise'

export interface ExerciseChallengeDto {
  id: number
  title: string
  category: string | null
  difficulty: string | null
  tags: string[] | null
  type: string | null
  credit: boolean
  content: string | null
  hints: string[] | null
  flags: { id: number; flag: string }[] | null
  acceptedCount: number
  submissionCount: number
}

export interface ExerciseDetailDto {
  id: number
  title: string
  category: string
  difficulty: string
  tags: string[] | null
  type: string
  credit: boolean
  content: string
  hints: string[] | null
  flags: {
    id: number
    orderIndex: number
    description: string | null
    customName: string | null
    answerType: string
    attachmentUrl: string | null
    attachmentFileSize: number | null
  }[]
  solvedFlagIds: number[]
  attempts: number
  limit: number | null
  solved: boolean
  queue: {
    status: string
    operation: string
    queuePosition: number
    peopleAhead: number
    targetNodeName: string | null
    stageMessage: string | null
    errorMessage: string | null
  } | null
  context: {
    closeTime: number | null
    instanceEntry: string | null
    instanceEntryStatus: ContainerEntryStatus | null
    instanceEntryReadyAt: number | null
    instanceEntryError: string | null
    url: string | null
    fileSize: number | null
  }
}

export interface ExerciseInfoDto {
  id: number
  title: string
  difficulty: string | null
  category: string | null
  tags: string[] | null
  credit: boolean
  type: string
  isEnabled: boolean
  acceptedCount: number
  submissionCount: number
  solved: boolean
  userAcceptedCount: number
  userSubmissionCount: number
  poolSource?: 'Exercise' | 'Game' | 'Training'
}

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  const body = await response.json().catch(() => null) as T | { message?: string; title?: string } | null
  if (!response.ok) {
    const detail = body && typeof body === 'object' && ('message' in body || 'title' in body)
      ? body.message || body.title
      : null
    throw new Error(detail || `Request failed with status ${response.status}`)
  }
  return body as T
}

export function useExercises(filter?: string) {
  const { data, error, mutate } = useSWR<ExerciseInfoDto[]>(
    filter ? `${BASE}?${filter}` : BASE,
    () => requestJson<ExerciseInfoDto[]>(filter ? `${BASE}?${filter}` : BASE)
  )
  return { data, error, mutate }
}

export function useExerciseDetail(id: number) {
  const { data, error, mutate } = useSWR<ExerciseDetailDto>(
    id > 0 ? `${BASE}/${id}` : null,
    () => requestJson<ExerciseDetailDto>(`${BASE}/${id}`)
  )
  return { data, error, mutate }
}

export async function submitFlag(id: number, flag: string, flagId?: number) {
  const body: Record<string, unknown> = { flag }
  if (flagId) body.flagId = flagId
  return requestJson<{ status: string; flagId: number | null }>(`${BASE}/${id}/flag`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export async function createContainer(id: number) {
  return requestJson(`${BASE}/${id}/container`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
}

export async function extendContainer(id: number) {
  return requestJson(`${BASE}/${id}/container/extend`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
}

export async function destroyContainer(id: number) {
  return requestJson(`${BASE}/${id}/container`, { method: 'DELETE' })
}
