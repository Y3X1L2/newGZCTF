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
}

export function useExercises(filter?: string) {
  const { data, error, mutate } = useSWR<ExerciseInfoDto[]>(
    filter ? `${BASE}?${filter}` : BASE,
    () => fetch(filter ? `${BASE}?${filter}` : BASE).then(r => r.json())
  )
  return { data, error, mutate }
}

export function useExerciseDetail(id: number) {
  const { data, error, mutate } = useSWR<ExerciseDetailDto>(
    id > 0 ? `${BASE}/${id}` : null,
    () => fetch(`${BASE}/${id}`).then(r => r.json())
  )
  return { data, error, mutate }
}

export async function submitFlag(id: number, flag: string, flagId?: number) {
  const body: Record<string, unknown> = { flag }
  if (flagId) body.flagId = flagId
  const response = await fetch(`${BASE}/${id}/flag`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return response.json()
}

export async function createContainer(id: number) {
  const response = await fetch(`${BASE}/${id}/container`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
  return response.json()
}
