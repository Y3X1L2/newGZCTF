import api, { Role } from '@Api'

export interface PublicUserTeam {
  id: number
  name: string
  avatar: string | null
}

export interface PublicUserCourse {
  id: number
  title: string
}

export interface PublicUserProfile {
  id: string
  userName: string
  role: Role
  bio: string
  avatar: string | null
  registeredAt: number
  publicTeam: PublicUserTeam | null
  taughtCourses: PublicUserCourse[]
}

export interface UserProfileMetrics {
  solved: number
  submissions: number
  acceptedSubmissions: number
  successRate: number
  gameCount: number
  courseCount: number
  activeDays: number
}

export interface UserSkillDimension {
  id: string
  label: string
  solved: number
  attempted: number
  submissions: number
  acceptedSubmissions: number
  successRate: number
  benchmarkP90: number
  radarValue: number
  sampleSufficient: boolean
}

export interface UserProfileTrendPoint {
  date: string
  cumulativeSolved: number
  delta: number
}

export interface UserProfileOverview {
  window: string
  generatedAt: number
  metrics: UserProfileMetrics
  dimensions: UserSkillDimension[]
  trend: UserProfileTrendPoint[]
}

export interface UserActivityPoint {
  date: string
  ctf: number
  training: number
  theory: number
  awdp: number
  penetration: number
  total: number
}

export interface UserProfileHistoryItem {
  id: string
  type: string
  occurredAt: number
  title: string
  summary: string
  route: string | null
}

export interface UserProfileHistoryPage {
  items: UserProfileHistoryItem[]
  nextCursor: string | null
}

export interface UserPrivateOverview {
  approvedCourses: number
  learningCourses: number
  completedCourses: number
  pendingEnrollments: number
  submittedTheoryAssignments: number
}

export interface AccountSummaryContinueItem {
  id: string
  kind: string
  title: string
  subtitle: string
  route: string
  endsAt: number | null
}

export interface AccountSummary {
  id: string
  userName: string
  role: Role
  bio: string
  avatar: string | null
  solved: number
  activeDays: number
  runningInstances: number
  pendingReviews: number
  continueItems: AccountSummaryContinueItem[]
}

export class UserProfileApiError extends Error {
  constructor(
    message: string,
    readonly status: number
  ) {
    super(message)
    this.name = 'UserProfileApiError'
  }
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : null
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  try {
    const response = await api.request<T>({ path, method: 'GET', format: 'json', signal })
    return response.data
  } catch (error) {
    const source = asRecord(error)
    const response = asRecord(source?.response)
    const payload = asRecord(response?.data)
    const status = typeof response?.status === 'number' ? response.status : 0
    const message =
      (typeof payload?.title === 'string' && payload.title) ||
      (typeof payload?.message === 'string' && payload.message) ||
      (typeof source?.message === 'string' && source.message) ||
      `请求失败 (${status || 'network'})`
    throw new UserProfileApiError(message, status)
  }
}

export const userProfileApi = {
  profile(userId: string, signal?: AbortSignal) {
    return getJson<PublicUserProfile>(`/api/users/${encodeURIComponent(userId)}`, signal)
  },
  overview(userId: string, window: string, signal?: AbortSignal) {
    const query = new URLSearchParams({ window })
    return getJson<UserProfileOverview>(`/api/users/${encodeURIComponent(userId)}/overview?${query}`, signal)
  },
  activity(userId: string, from: string, to: string, signal?: AbortSignal) {
    const query = new URLSearchParams({ from, to })
    return getJson<UserActivityPoint[]>(`/api/users/${encodeURIComponent(userId)}/activity?${query}`, signal)
  },
  history(userId: string, type: string, cursor: string | null, count = 20, signal?: AbortSignal) {
    const query = new URLSearchParams({ type, count: String(count) })
    if (cursor) query.set('cursor', cursor)
    return getJson<UserProfileHistoryPage>(`/api/users/${encodeURIComponent(userId)}/history?${query}`, signal)
  },
  privateOverview(signal?: AbortSignal) {
    return getJson<UserPrivateOverview>('/api/users/me/private-overview', signal)
  },
  accountSummary(signal?: AbortSignal) {
    return getJson<AccountSummary>('/api/Account/Summary', signal)
  },
}
