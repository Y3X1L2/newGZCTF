import api, {
  AnswerResult,
  ChallengeCategory,
  ChallengeType,
  ContainerInfoModel,
  ContentType,
  EnvironmentType,
  FlagSubmitModel,
  Role,
} from '@Api'

export enum TrainingType {
  Ctf = 'Ctf',
  Theory = 'Theory',
}

export enum TrainingVisibilityType {
  GroupOnly = 'GroupOnly',
  AllStudents = 'AllStudents',
}

export enum TrainingModuleProgressStatus {
  NotStarted = 'NotStarted',
  Reading = 'Reading',
  Practicing = 'Practicing',
  Completed = 'Completed',
}

export interface StudentGroupBriefModel {
  id: number
  name: string
  description: string
  isArchived: boolean
  memberCount: number
  managerCount: number
  updatedAt: number
}

export interface StudentGroupDetailModel extends StudentGroupBriefModel {
  members: StudentGroupMemberModel[]
  managers: StudentGroupManagerModel[]
}

export interface StudentGroupMemberModel {
  studentId: string
  userName: string
  realName: string
  stdNumber: string
  avatar?: string | null
  note: string
  joinedAt: number
}

export interface StudentGroupManagerModel {
  teacherId: string
  userName: string
  realName: string
  roleInGroup: string
}

export interface StudentGroupEditModel {
  name: string
  description: string
}

export interface TrainingCompletionRule {
  requireArticleRead: boolean
  requireAllRequiredChallenges: boolean
  requiredChallengeCount: number
  theoryPassRate: number
}

export interface TrainingDirectionEditModel {
  type: TrainingType
  key: string
  title: string
  description: string
  icon: string
  color: string
  order: number
  isEnabled: boolean
}

export interface TrainingDirectionModel extends TrainingDirectionEditModel {
  id: number
  modules: TrainingModuleModel[]
}

export interface TrainingModuleEditModel {
  directionId: number
  parentId?: number | null
  type: TrainingType
  title: string
  slug: string
  summary: string
  articleContent: string
  articleContentType: 'Markdown' | 'Html'
  environmentTemplateId?: number | null
  completionRule: TrainingCompletionRule
  order: number
}

export interface TrainingModuleModel extends TrainingModuleEditModel {
  id: number
  environmentTemplateName?: string | null
  isPublished: boolean
  publishedAt?: number | null
  visibilities: TrainingModuleVisibilityModel[]
  challenges: TrainingModuleChallengeModel[]
  progressStatus?: TrainingModuleProgressStatus | null
  challengeSolvedCount: number
  challengeTotalCount: number
}

export interface TrainingModuleVisibilityModel {
  id: number
  visibilityType: TrainingVisibilityType
  groupId?: number | null
  groupName?: string | null
}

export interface TrainingModuleVisibilityEditModel {
  visibilityType: TrainingVisibilityType
  groupId?: number | null
}

export interface TrainingModuleChallengeModel {
  exerciseChallengeId: number
  title: string
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  order: number
  isRequired: boolean
  displayTitle?: string | null
}

export interface TrainingCtfChallengeDetailModel {
  id: number
  moduleId: number
  title: string
  content: string
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  hints?: string[] | null
  difficulty: string | number
  tags?: string[] | null
  solved: boolean
  attempts: number
  limit: number
  flags?: { id?: number; orderIndex?: number; description?: string | null }[] | null
  context: {
    closeTime?: number | null
    instanceEntry?: string | null
    url?: string | null
    fileSize?: number | null
  }
}

export interface TrainingSubmitResultModel {
  submissionId: number
  status: AnswerResult
  moduleCompleted: boolean
}

export interface TheoryTrainingPlanEditModel {
  title: string
  description: string
  mode: 'Random' | 'Manual'
  questionCount: number
  bankName?: string | null
  questionTypes?: string[] | null
  passRate: number
  allowRetake: boolean
  showCorrectAnswerAfterSubmit: boolean
  isPublished: boolean
  questions: { sourceQuestionId: number; score: number; order: number }[]
}

export interface TheoryTrainingPlanModel extends TheoryTrainingPlanEditModel {
  id: number
  moduleId: number
  updatedAt: number
}

export interface TheoryTrainingSessionModel {
  id: number
  moduleId: number
  status: 'Draft' | 'Submitted'
  score: number
  maxScore: number
  correctCount: number
  totalCount: number
  passRate: number
  correctRate: number
  passed: boolean
  createdAt: number
  submittedAt?: number | null
  questions: {
    id: number
    type: string
    title: string
    content: string
    options: string[]
    score: number
    order: number
    isCorrect?: boolean | null
    selectedIndexes: number[]
    answerIndexes?: number[] | null
  }[]
}

export interface TheoryTrainingSessionSubmitModel {
  answers: { questionId: number; selectedIndexes: number[] }[]
}

export interface TrainingGroupStatsModel {
  groupId: number
  groupName: string
  studentCount: number
  totalModules: number
  averageCompletionRate: number
  students: {
    userId: string
    userName: string
    realName?: string | null
    totalModules: number
    completedModules: number
    ctfSolvedChallenges: number
    ctfTotalChallenges: number
    theoryCompletedModules: number
    theoryTotalModules: number
    lastActivity?: number | null
  }[]
}

export interface TrainingOverviewModel {
  totalModules: number
  completedModules: number
  ctfSolvedChallenges: number
  ctfTotalChallenges: number
  theoryCompletedModules: number
  theoryTotalModules: number
  completionRate: number
}

const request = api.request

export const roleLabel = (role?: Role | null) => {
  switch (role) {
    case Role.SuperAdmin:
      return '超级管理员'
    case Role.Admin:
      return '管理员'
    case Role.Teacher:
    case Role.Monitor:
      return '老师'
    case Role.Student:
    case Role.User:
      return '学生'
    case Role.Banned:
      return '禁用'
    default:
      return '未知'
  }
}

export const trainingApi = {
  catalog: () =>
    request<TrainingDirectionModel[], unknown>({
      path: '/api/training/catalog',
      method: 'GET',
    }),

  overview: () =>
    request<TrainingOverviewModel, unknown>({
      path: '/api/training/overview',
      method: 'GET',
    }),

  getModule: (moduleId: number) =>
    request<TrainingModuleModel, unknown>({
      path: `/api/training/modules/${moduleId}`,
      method: 'GET',
    }),

  markRead: (moduleId: number) =>
    request<void, unknown>({
      path: `/api/training/modules/${moduleId}/read`,
      method: 'POST',
    }),

  ctfChallenges: (moduleId: number) =>
    request<TrainingModuleChallengeModel[], unknown>({
      path: `/api/training/ctf/modules/${moduleId}/challenges`,
      method: 'GET',
    }),

  ctfChallenge: (moduleId: number, challengeId: number) =>
    request<TrainingCtfChallengeDetailModel, unknown>({
      path: `/api/training/ctf/modules/${moduleId}/challenges/${challengeId}`,
      method: 'GET',
    }),

  createCtfContainer: (moduleId: number, challengeId: number) =>
    request<ContainerInfoModel, unknown>({
      path: `/api/training/ctf/modules/${moduleId}/challenges/${challengeId}/container`,
      method: 'POST',
    }),

  destroyCtfContainer: (moduleId: number, challengeId: number) =>
    request<void, unknown>({
      path: `/api/training/ctf/modules/${moduleId}/challenges/${challengeId}/container`,
      method: 'DELETE',
    }),

  submitCtfFlag: (moduleId: number, challengeId: number, data: FlagSubmitModel) =>
    request<TrainingSubmitResultModel, unknown>({
      path: `/api/training/ctf/modules/${moduleId}/challenges/${challengeId}/submit`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  theorySession: (moduleId: number) =>
    request<TheoryTrainingSessionModel, unknown>({
      path: `/api/training/theory/modules/${moduleId}/session`,
      method: 'GET',
    }),

  regenerateTheorySession: (moduleId: number) =>
    request<TheoryTrainingSessionModel, unknown>({
      path: `/api/training/theory/modules/${moduleId}/session/regenerate`,
      method: 'POST',
    }),

  submitTheorySession: (sessionId: number, data: TheoryTrainingSessionSubmitModel) =>
    request<TheoryTrainingSessionModel, unknown>({
      path: `/api/training/theory/sessions/${sessionId}/submit`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),
}

export const trainingAdminApi = {
  groups: () =>
    request<StudentGroupBriefModel[], unknown>({
      path: '/api/admin/student-groups',
      method: 'GET',
    }),

  group: (groupId: number) =>
    request<StudentGroupDetailModel, unknown>({
      path: `/api/admin/student-groups/${groupId}`,
      method: 'GET',
    }),

  createGroup: (data: StudentGroupEditModel) =>
    request<StudentGroupDetailModel, unknown>({
      path: '/api/admin/student-groups',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  addGroupMember: (groupId: number, data: { studentId: string; note?: string }) =>
    request<void, unknown>({
      path: `/api/admin/student-groups/${groupId}/members`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  removeGroupMember: (groupId: number, studentId: string) =>
    request<void, unknown>({
      path: `/api/admin/student-groups/${groupId}/members/${studentId}`,
      method: 'DELETE',
    }),

  directions: (type?: TrainingType) =>
    request<TrainingDirectionModel[], unknown>({
      path: '/api/admin/training/directions',
      method: 'GET',
      query: { type },
    }),

  createDirection: (data: TrainingDirectionEditModel) =>
    request<TrainingDirectionModel, unknown>({
      path: '/api/admin/training/directions',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  modules: (type?: TrainingType, directionId?: number) =>
    request<TrainingModuleModel[], unknown>({
      path: '/api/admin/training/modules',
      method: 'GET',
      query: { type, directionId },
    }),

  createModule: (data: TrainingModuleEditModel) =>
    request<TrainingModuleModel, unknown>({
      path: '/api/admin/training/modules',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateModule: (moduleId: number, data: TrainingModuleEditModel) =>
    request<void, unknown>({
      path: `/api/admin/training/modules/${moduleId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  setVisibility: (moduleId: number, data: TrainingModuleVisibilityEditModel[]) =>
    request<void, unknown>({
      path: `/api/admin/training/modules/${moduleId}/visibility`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  publish: (moduleId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/modules/${moduleId}/publish`,
      method: 'POST',
    }),

  unpublish: (moduleId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/modules/${moduleId}/unpublish`,
      method: 'POST',
    }),

  addModuleChallenge: (
    moduleId: number,
    data: { exerciseChallengeId: number; order: number; isRequired: boolean; displayTitle?: string | null }
  ) =>
    request<void, unknown>({
      path: `/api/admin/training/modules/${moduleId}/challenges`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  copyGameChallenge: (moduleId: number, challengeId: number) =>
    request<TrainingModuleChallengeModel, unknown>({
      path: `/api/admin/training/modules/${moduleId}/challenges/from-game-challenge/${challengeId}`,
      method: 'POST',
    }),

  theoryPlan: (moduleId: number) =>
    request<TheoryTrainingPlanModel, unknown>({
      path: `/api/admin/training/modules/${moduleId}/theory-plan`,
      method: 'GET',
    }),

  saveTheoryPlan: (moduleId: number, data: TheoryTrainingPlanEditModel) =>
    request<TheoryTrainingPlanModel, unknown>({
      path: `/api/admin/training/modules/${moduleId}/theory-plan`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  statsOverview: () =>
    request<TrainingGroupStatsModel[], unknown>({
      path: '/api/admin/training/stats/overview',
      method: 'GET',
    }),

  groupStats: (groupId: number) =>
    request<TrainingGroupStatsModel, unknown>({
      path: `/api/admin/training/stats/groups/${groupId}`,
      method: 'GET',
    }),
}
