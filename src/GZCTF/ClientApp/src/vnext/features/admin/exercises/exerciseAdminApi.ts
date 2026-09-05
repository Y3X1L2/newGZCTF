import useSWR from 'swr'
import api, {
  AnswerType,
  ChallengeCategory,
  ChallengeType,
  Difficulty,
  EnvironmentType,
  FileType,
  FlagScoreMode,
  NetworkMode,
} from '@Api'
import { ExerciseInfoDto } from '../../practice/api/practiceApi'

const BASE = '/api/exercise'
const MANAGEMENT_LIST = `${BASE}/manage`

export interface ExerciseAdminFlag {
  id?: number | null
  flag: string
  orderIndex: number
  description?: string | null
  scoreMode: FlagScoreMode
  fixedScore: number
  maxAttempts: number
  attachmentHash?: string | null
  answerType: AnswerType
  customName?: string | null
  attachmentType: FileType
  fileHash?: string | null
  remoteUrl?: string | null
}

export interface ExerciseAdminDraft {
  id?: number
  title: string
  content: string
  category: ChallengeCategory
  type: ChallengeType
  difficulty: Difficulty
  credit: boolean
  isEnabled: boolean
  tags: string[]
  hints: string[]
  containerImage: string | null
  memoryLimit: number | null
  storageLimit: number | null
  cpuCount: number | null
  exposePort: number | null
  networkMode: NetworkMode
  environment: EnvironmentType
  imageTemplateId: number | null
  flagTemplate: string | null
  submissionLimit: number
  flags: ExerciseAdminFlag[]
  attachment: {
    attachmentType: FileType
    fileHash?: string | null
    remoteUrl?: string | null
  } | null
}

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  const body = await response.json().catch(() => null) as T | { message?: string; title?: string } | null
  if (!response.ok) {
    const problem = body as { message?: string; title?: string } | null
    throw new Error(problem?.message || problem?.title || `Request failed with status ${response.status}`)
  }
  return body as T
}

export function useAdminExercises() {
  return useSWR<ExerciseInfoDto[]>(MANAGEMENT_LIST, () => exerciseAdminApi.list(), {
    revalidateOnFocus: false,
  })
}

export async function uploadExerciseAsset(file: File) {
  const response = await api.assets.assetsUpload({ files: [file] }, { filename: file.name })
  const hash = response.data?.[0]?.hash
  if (!hash) throw new Error('文件已上传，但服务器没有返回可用的文件标识。')
  return hash
}

export const exerciseAdminApi = {
  list() {
    return requestJson<ExerciseInfoDto[]>(MANAGEMENT_LIST)
  },
  detail(id: number) {
    return requestJson<ExerciseAdminDraft>(`${BASE}/${id}/manage`)
  },
  create(payload: ExerciseAdminDraft) {
    return requestJson<ExerciseAdminDraft>(BASE, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },
  update(id: number, payload: ExerciseAdminDraft) {
    return requestJson<ExerciseAdminDraft>(`${BASE}/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },
  remove(id: number) {
    return requestJson<null>(`${BASE}/${id}`, { method: 'DELETE' })
  },
}
