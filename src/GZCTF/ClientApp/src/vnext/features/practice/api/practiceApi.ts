import useSWR from 'swr'

const BASE = '/api/exercise'

export interface ExerciseChallengeDto {
  id: number
  title: string
  category: string | null
  difficulty: string | null
  tags: string[] | null
  type: string | null
  credit: number | null
  content: string | null
  hints: string[] | null
  flags: { id: number; flag: string }[] | null
  acceptedCount: number
  submissionCount: number
}

export interface ExerciseContainerDto {
  entry: string | null
  entryStatus: string | null
  entryReadyAt: number | null
  entryError: string | null
  closeTime: number | null
  error: string | null
}

export interface ExerciseDetailDto {
  exercise: ExerciseChallengeDto | null
  container: ExerciseContainerDto | null
}

export interface ExerciseInfoDto {
  id: number
  title: string
  difficulty: string | null
  category: string | null
  tags: string[] | null
  credit: number | null
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
