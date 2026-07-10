import api, {
  AnswerResult,
  ChallengeCategory,
  ChallengeType,
  ContainerInfoModel,
  ContentType,
  EnvironmentType,
  FileType,
  FlagSubmitModel,
  NetworkMode,
  Role,
} from '@Api'
import {
  TheoryAnswerModel,
  TheoryAnswerSheetEditModel,
  TheoryAnswerSheetStatus,
  TheoryQuestionEditModel,
  TheoryQuestionType,
} from '../Api/TheoryApi'

export enum TrainingCourseStatus {
  Draft = 'Draft',
  Published = 'Published',
  Archived = 'Archived',
}

export enum TrainingCourseEnrollmentPolicy {
  TeacherApproval = 'TeacherApproval',
  AutoApprove = 'AutoApprove',
}

export enum TrainingCourseEnrollmentStatus {
  Pending = 'Pending',
  Approved = 'Approved',
  Rejected = 'Rejected',
  Cancelled = 'Cancelled',
}

export enum TrainingCourseTeacherRole {
  Owner = 'Owner',
  Teacher = 'Teacher',
}

export enum TrainingCourseResourceType {
  File = 'File',
  Link = 'Link',
  Video = 'Video',
}

export enum TrainingCourseVideoProvider {
  None = 'None',
  LocalFile = 'LocalFile',
  ExternalUrl = 'ExternalUrl',
}

export enum TrainingCourseProgressStatus {
  NotStarted = 'NotStarted',
  Learning = 'Learning',
  Completed = 'Completed',
}

export interface TrainingChapterCompletionPolicy {
  requireContentRead: boolean
  requireAllRequiredChallenges: boolean
  requiredChallengeCount: number
  theoryPassRate: number
}

export interface TrainingCourseEditModel {
  title: string
  slug: string
  summary: string
  description: string
  coverFileHash?: string | null
  tags: string[]
  enrollmentPolicy: TrainingCourseEnrollmentPolicy
}

export interface TrainingCourseTeacherModel {
  teacherId: string
  userName: string
  realName: string
  role: TrainingCourseTeacherRole
  assignedAt: number
}

export interface TrainingCourseEnrollmentModel {
  courseId: number
  userId: string
  userName: string
  realName: string
  stdNumber: string
  status: TrainingCourseEnrollmentStatus
  applyReason: string
  reviewComment: string
  requestedAt: number
  reviewedAt?: number | null
  completedChapterCount: number
  totalChapterCount: number
  progressStatus?: TrainingCourseProgressStatus | null
  progressUpdatedAt?: number | null
}

export interface TrainingCourseTeacherCandidateModel {
  userId: string
  userName: string
  realName: string
  stdNumber: string
  email?: string | null
  role: Role
  alreadyTeacher: boolean
}

export interface TrainingCourseStudentCandidateModel {
  userId: string
  userName: string
  realName: string
  stdNumber: string
  email?: string | null
  avatar?: string | null
  alreadyEnrolled: boolean
}

export interface TrainingCourseStudentLearningSummaryModel {
  userId: string
  userName: string
  realName: string
  stdNumber: string
  enrollmentStatus: TrainingCourseEnrollmentStatus
  completedChapterCount: number
  totalChapterCount: number
  challengeSolvedCount: number
  challengeTotalCount: number
  theorySubmittedCount: number
  theoryPassedCount: number
  theoryTotalCount: number
  theoryScore: number
  theoryMaxScore: number
  progressStatus?: TrainingCourseProgressStatus | null
  lastActivityAt?: number | null
}

export interface TrainingCourseStudentLearningDetailModel extends TrainingCourseStudentLearningSummaryModel {
  chapters: TrainingCourseStudentChapterLearningModel[]
}

export interface TrainingCourseStudentChapterLearningModel {
  chapterId: number
  title: string
  summary: string
  order: number
  isPublished: boolean
  completionPolicy: TrainingChapterCompletionPolicy
  progressStatus?: TrainingCourseProgressStatus | null
  readPercent: number
  completedAt?: number | null
  theory?: TrainingCourseStudentTheoryLearningModel | null
  challenges: TrainingCourseStudentChallengeLearningModel[]
}

export interface TrainingCourseStudentChallengeLearningModel {
  exerciseChallengeId: number
  title: string
  displayTitle?: string | null
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  isRequired: boolean
  solved: boolean
  submissionCount: number
  acceptedSubmissionCount: number
  lastStatus?: AnswerResult | null
  lastSubmittedAt?: number | null
  lastIpAddress?: string | null
  instanceEntry?: string | null
  instanceStopAt?: number | null
}

export interface TrainingCourseStudentTheoryLearningModel {
  paperId: number
  title: string
  isPublished: boolean
  questionCount: number
  totalScore: number
  passRate: number
  status?: TheoryAnswerSheetStatus | null
  score?: number | null
  passed?: boolean | null
  correctCount: number
  submittedAt?: number | null
  answers: TrainingCourseStudentTheoryAnswerDetailModel[]
}

export interface TrainingCourseStudentTheoryAnswerDetailModel {
  questionId: number
  type: TheoryQuestionType
  title: string
  content: string
  options: string[]
  answerIndexes: number[]
  selectedIndexes: number[]
  isCorrect?: boolean | null
  score: number
  maxScore: number
  order: number
}

export interface TrainingCourseResourceModel {
  id: number
  courseId: number
  title: string
  description: string
  type: TrainingCourseResourceType
  externalUrl?: string | null
  fileName?: string | null
  fileSize?: number | null
  downloadUrl?: string | null
  order: number
  isVisible: boolean
  createdAt: number
}

export interface TrainingCourseChallengeModel {
  exerciseChallengeId: number
  chapterId?: number | null
  title: string
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  order: number
  isRequired: boolean
  solved: boolean
  displayTitle?: string | null
  hasAttachment?: boolean
  attachmentFileName?: string | null
}

export interface TrainingCourseChapterModel {
  id: number
  courseId: number
  parentId?: number | null
  title: string
  summary: string
  content: string
  contentType: 'Markdown' | 'Html'
  completionPolicy: TrainingChapterCompletionPolicy
  videoProvider: TrainingCourseVideoProvider
  videoUrl?: string | null
  videoFileUrl?: string | null
  order: number
  isPublished: boolean
  progressStatus?: TrainingCourseProgressStatus | null
  readPercent: number
  completedAt?: number | null
  challenges: TrainingCourseChallengeModel[]
  theoryPaper?: TrainingCourseChapterTheorySummaryModel | null
}

export interface TrainingCourseModel extends TrainingCourseEditModel {
  id: number
  coverUrl?: string | null
  status: TrainingCourseStatus
  enrollmentStatus?: TrainingCourseEnrollmentStatus | null
  canLearn: boolean
  canEdit: boolean
  canManageTeachers: boolean
  canManageEnrollments: boolean
  canDelete: boolean
  chapterCount: number
  resourceCount: number
  enrollmentCount: number
  completedChapterCount: number
  totalChapterCount: number
  progressStatus?: TrainingCourseProgressStatus | null
  lastStudiedAt?: number | null
  createdAt: number
  updatedAt: number
  teachers: TrainingCourseTeacherModel[]
  chapters: TrainingCourseChapterModel[]
  resources: TrainingCourseResourceModel[]
  challenges: TrainingCourseChallengeModel[]
}

export interface TrainingCourseEnrollmentApplyModel {
  applyReason: string
}

export interface TrainingCourseEnrollmentReviewModel {
  status: TrainingCourseEnrollmentStatus
  reviewComment: string
}

export interface TrainingCourseChapterEditModel {
  parentId?: number | null
  title: string
  summary: string
  content: string
  contentType: 'Markdown' | 'Html'
  completionPolicy: TrainingChapterCompletionPolicy
  videoProvider: TrainingCourseVideoProvider
  videoUrl?: string | null
  videoFileHash?: string | null
  order: number
  isPublished: boolean
}

export interface TrainingCourseResourceEditModel {
  title: string
  description: string
  type: TrainingCourseResourceType
  externalUrl?: string | null
  localFileHash?: string | null
  order: number
  isVisible: boolean
}

export interface TrainingCourseChallengeEditModel {
  exerciseChallengeId: number
  chapterId?: number | null
  order: number
  isRequired: boolean
  displayTitle?: string | null
}

export interface TrainingCourseImageTemplateModel {
  id: number
  name: string
  osType: string | number
  imageType: string | number
  status: string | number
  fileSize: number
  description?: string | null
  errorMessage?: string | null
  imageHash?: string | null
  registryUrl?: string | null
  uploadedAt: number
  trainingCourseId?: number | null
}

export interface TrainingCourseDockerRegisterModel {
  name: string
  registryUrl: string
  osType: string | number
  registryAuth?: string | null
}

export interface TrainingCourseLocalImageImportModel {
  localPath: string
  displayName?: string | null
}

export interface TrainingCourseChallengeCreateModel {
  title: string
  content: string
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  imageTemplateId?: number | null
  containerImage?: string | null
  memoryLimit?: number | null
  cpuCount?: number | null
  storageLimit?: number | null
  exposePort?: number | null
  networkMode?: NetworkMode | null
  flagTemplate?: string | null
  staticFlag?: string | null
  submissionLimit: number
  chapterId?: number | null
  order: number
  isRequired: boolean
  displayTitle?: string | null
  attachmentType?: FileType
  attachmentFileHash?: string | null
  attachmentRemoteUrl?: string | null
}

export interface TrainingCourseChallengeEditDetailModel extends TrainingCourseChallengeCreateModel {
  exerciseChallengeId: number
  attachmentUrl?: string | null
  attachmentFileName?: string | null
  attachmentFileSize?: number | null
  submissionCount: number
  hasSubmittedAnswers: boolean
}

export interface TrainingCourseTheoryQuestionModel extends TheoryQuestionEditModel {
  id: number
  courseId: number
  createdAt: number
  updatedAt: number
}

export interface TrainingCourseTheoryPaperQuestionEditModel extends TheoryQuestionEditModel {
  id?: number
  sourceQuestionId?: number | null
  score: number
  order: number
}

export interface TrainingCourseChapterTheoryPaperEditModel {
  title: string
  description: string
  passRate: number
  allowRetake: boolean
  showCorrectAnswerAfterSubmit: boolean
  isPublished: boolean
  questions: TrainingCourseTheoryPaperQuestionEditModel[]
}

export interface TrainingCourseChapterTheorySummaryModel {
  id: number
  courseId: number
  chapterId: number
  title: string
  isPublished: boolean
  questionCount: number
  totalScore: number
  passRate: number
  allowRetake: boolean
  showCorrectAnswerAfterSubmit: boolean
  attemptNumber?: number | null
  status?: TheoryAnswerSheetStatus | null
  score?: number | null
  passed?: boolean | null
  submittedAt?: number | null
}

export interface TrainingCourseChapterTheoryPaperDetailModel extends TrainingCourseChapterTheoryPaperEditModel {
  id: number
  courseId: number
  chapterId: number
  publishedAt?: number | null
  updatedAt?: number
  totalScore: number
}

export interface TrainingCourseChapterTheoryPlayerQuestionModel {
  id: number
  type: TheoryQuestionType
  title: string
  content: string
  options: string[]
  score: number
  order: number
  answerIndexes?: number[] | null
}

export interface TrainingCourseChapterTheoryPlayerPaperModel {
  paperId: number
  courseId: number
  chapterId: number
  title: string
  description: string
  totalScore: number
  passRate: number
  allowRetake: boolean
  showCorrectAnswerAfterSubmit: boolean
  attemptNumber?: number | null
  status?: TheoryAnswerSheetStatus | null
  score?: number | null
  passed?: boolean | null
  submittedAt?: number | null
  updatedAt?: number | null
  questions: TrainingCourseChapterTheoryPlayerQuestionModel[]
  answers: TheoryAnswerModel[]
}

export interface TrainingCourseDockerRegistryModel {
  enabled: boolean
  address: string
  namespace: string
  maxUploadSizeGb: number
}

export interface TrainingCourseSubmitResultModel {
  submissionId: number
  status: AnswerResult
  chapterCompleted: boolean
  courseCompleted: boolean
}

export interface TrainingCourseChallengeDetailModel {
  courseId: number
  chapterId?: number | null
  id: number
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

export interface TrainingCheckInModel {
  date: string
  checkedAt: number
  isToday: boolean
}

export interface TrainingActivityPointModel {
  date: string
  studyActions: number
  completedChapters: number
  acceptedChallenges: number
  checkedIn: boolean
}

export interface TrainingPersonalOverviewModel {
  visibleCourseCount: number
  joinedCourseCount: number
  completedCourseCount: number
  averageProgress: number
  completedChapterCount: number
  totalChapterCount: number
  ctfSolvedChallenges: number
  ctfTotalChallenges: number
  theoryPassedAssessments: number
  theoryTotalAssessments: number
  checkInDays: number
  currentCheckInStreak: number
  checkedInToday: boolean
  checkIns: TrainingCheckInModel[]
  activity: TrainingActivityPointModel[]
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

export const trainingCourseApi = {
  courses: () =>
    request<TrainingCourseModel[], unknown>({
      path: '/api/training/courses',
      method: 'GET',
    }),

  overview: () =>
    request<TrainingPersonalOverviewModel, unknown>({
      path: '/api/training/courses/overview',
      method: 'GET',
    }),

  checkIn: () =>
    request<TrainingPersonalOverviewModel, unknown>({
      path: '/api/training/courses/check-in',
      method: 'POST',
    }),

  course: (courseId: number) =>
    request<TrainingCourseModel, unknown>({
      path: `/api/training/courses/${courseId}`,
      method: 'GET',
    }),

  enroll: (courseId: number, data: TrainingCourseEnrollmentApplyModel) =>
    request<TrainingCourseEnrollmentModel, unknown>({
      path: `/api/training/courses/${courseId}/enroll`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  cancelEnroll: (courseId: number) =>
    request<void, unknown>({
      path: `/api/training/courses/${courseId}/enroll`,
      method: 'DELETE',
    }),

  chapter: (courseId: number, chapterId: number) =>
    request<TrainingCourseChapterModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}`,
      method: 'GET',
    }),

  completeChapter: (courseId: number, chapterId: number) =>
    request<TrainingCourseChapterModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}/complete`,
      method: 'POST',
    }),

  challenge: (courseId: number, challengeId: number, chapterId?: number | null) =>
    request<TrainingCourseChallengeDetailModel, unknown>({
      path: `/api/training/courses/${courseId}/challenges/${challengeId}`,
      method: 'GET',
      query: { chapterId },
    }),

  createContainer: (courseId: number, challengeId: number, chapterId?: number | null) =>
    request<ContainerInfoModel, unknown>({
      path: `/api/training/courses/${courseId}/challenges/${challengeId}/container`,
      method: 'POST',
      query: { chapterId },
    }),

  extendContainer: (courseId: number, challengeId: number, chapterId?: number | null) =>
    request<ContainerInfoModel, unknown>({
      path: `/api/training/courses/${courseId}/challenges/${challengeId}/container/extend`,
      method: 'POST',
      query: { chapterId },
    }),

  destroyContainer: (courseId: number, challengeId: number, chapterId?: number | null) =>
    request<void, unknown>({
      path: `/api/training/courses/${courseId}/challenges/${challengeId}/container`,
      method: 'DELETE',
      query: { chapterId },
    }),

  submitFlag: (courseId: number, challengeId: number, data: FlagSubmitModel, chapterId?: number | null) =>
    request<TrainingCourseSubmitResultModel, unknown>({
      path: `/api/training/courses/${courseId}/challenges/${challengeId}/submit`,
      method: 'POST',
      query: { chapterId },
      body: data,
      type: ContentType.Json,
    }),

  chapterTheory: (courseId: number, chapterId: number) =>
    request<TrainingCourseChapterTheoryPlayerPaperModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory`,
      method: 'GET',
    }),

  retryChapterTheory: (courseId: number, chapterId: number) =>
    request<TrainingCourseChapterTheoryPlayerPaperModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/retry`,
      method: 'POST',
    }),

  saveChapterTheoryDraft: (courseId: number, chapterId: number, data: TheoryAnswerSheetEditModel) =>
    request<TrainingCourseChapterTheoryPlayerPaperModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/draft`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  submitChapterTheory: (courseId: number, chapterId: number, data: TheoryAnswerSheetEditModel) =>
    request<TrainingCourseChapterTheoryPlayerPaperModel, unknown>({
      path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/submit`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),
}

export const trainingCourseAdminApi = {
  courses: () =>
    request<TrainingCourseModel[], unknown>({
      path: '/api/admin/training/courses',
      method: 'GET',
    }),

  course: (courseId: number) =>
    request<TrainingCourseModel, unknown>({
      path: `/api/admin/training/courses/${courseId}`,
      method: 'GET',
    }),

  createCourse: (data: TrainingCourseEditModel) =>
    request<TrainingCourseModel, unknown>({
      path: '/api/admin/training/courses',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateCourse: (courseId: number, data: TrainingCourseEditModel) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  publish: (courseId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/publish`,
      method: 'POST',
    }),

  archive: (courseId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/archive`,
      method: 'POST',
    }),

  draft: (courseId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/draft`,
      method: 'POST',
    }),

  deleteCourse: (courseId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}`,
      method: 'DELETE',
    }),

  enrollments: (courseId: number) =>
    request<TrainingCourseEnrollmentModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/enrollments`,
      method: 'GET',
    }),

  learningSummaries: (courseId: number) =>
    request<TrainingCourseStudentLearningSummaryModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/learning-summaries`,
      method: 'GET',
    }),

  studentLearningDetail: (courseId: number, userId: string) =>
    request<TrainingCourseStudentLearningDetailModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/students/${userId}/learning`,
      method: 'GET',
    }),

  reviewEnrollment: (courseId: number, userId: string, data: TrainingCourseEnrollmentReviewModel) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/enrollments/${userId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  studentCandidates: (courseId: number, keyword?: string) =>
    request<TrainingCourseStudentCandidateModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/student-candidates`,
      method: 'GET',
      query: { keyword },
    }),

  addEnrollment: (courseId: number, data: { userId: string }) =>
    request<TrainingCourseEnrollmentModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/enrollments`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  teacherCandidates: (courseId: number, keyword?: string) =>
    request<TrainingCourseTeacherCandidateModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/teacher-candidates`,
      method: 'GET',
      query: { keyword },
    }),

  addTeacher: (courseId: number, data: { teacherId: string; role: TrainingCourseTeacherRole }) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/teachers`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  removeTeacher: (courseId: number, teacherId: string) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/teachers/${teacherId}`,
      method: 'DELETE',
    }),

  createChapter: (courseId: number, data: TrainingCourseChapterEditModel) =>
    request<TrainingCourseChapterModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/chapters`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateChapter: (courseId: number, chapterId: number, data: TrainingCourseChapterEditModel) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  deleteChapter: (courseId: number, chapterId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}`,
      method: 'DELETE',
    }),

  createResource: (courseId: number, data: TrainingCourseResourceEditModel) =>
    request<TrainingCourseResourceModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/resources`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateResource: (courseId: number, resourceId: number, data: TrainingCourseResourceEditModel) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/resources/${resourceId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  deleteResource: (courseId: number, resourceId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/resources/${resourceId}`,
      method: 'DELETE',
    }),

  addChallenge: (courseId: number, data: TrainingCourseChallengeEditModel) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/challenges`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  createChallenge: (courseId: number, data: TrainingCourseChallengeCreateModel) =>
    request<TrainingCourseChallengeModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/challenges/create`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  challengeEditDetail: (courseId: number, exerciseChallengeId: number) =>
    request<TrainingCourseChallengeEditDetailModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}/edit`,
      method: 'GET',
    }),

  updateChallenge: (courseId: number, exerciseChallengeId: number, data: TrainingCourseChallengeCreateModel) =>
    request<TrainingCourseChallengeEditDetailModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  removeChallenge: (courseId: number, exerciseChallengeId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}`,
      method: 'DELETE',
    }),

  imageTemplates: (courseId: number) =>
    request<TrainingCourseImageTemplateModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates`,
      method: 'GET',
    }),

  dockerRegistry: (courseId: number) =>
    request<TrainingCourseDockerRegistryModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates/docker-registry`,
      method: 'GET',
    }),

  registerDockerTemplate: (courseId: number, data: TrainingCourseDockerRegisterModel) =>
    request<TrainingCourseImageTemplateModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates/register-docker`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  importLocalTemplate: (courseId: number, data: TrainingCourseLocalImageImportModel) =>
    request<TrainingCourseImageTemplateModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates/import-local`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  attachImageTemplate: (courseId: number, templateId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates`,
      method: 'POST',
      body: { templateId },
      type: ContentType.Json,
    }),

  detachImageTemplate: (courseId: number, templateId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/image-templates/${templateId}`,
      method: 'DELETE',
    }),

  theoryQuestions: (courseId: number, query?: { keyword?: string; type?: TheoryQuestionType; bankName?: string; count?: number }) =>
    request<TrainingCourseTheoryQuestionModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/theory-questions`,
      method: 'GET',
      query,
    }),

  createTheoryQuestion: (courseId: number, data: TheoryQuestionEditModel) =>
    request<TrainingCourseTheoryQuestionModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/theory-questions`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateTheoryQuestion: (courseId: number, questionId: number, data: TheoryQuestionEditModel) =>
    request<TrainingCourseTheoryQuestionModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/theory-questions/${questionId}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  deleteTheoryQuestion: (courseId: number, questionId: number) =>
    request<void, unknown>({
      path: `/api/admin/training/courses/${courseId}/theory-questions/${questionId}`,
      method: 'DELETE',
    }),

  theoryPapers: (courseId: number) =>
    request<TrainingCourseChapterTheorySummaryModel[], unknown>({
      path: `/api/admin/training/courses/${courseId}/theory-papers`,
      method: 'GET',
    }),

  chapterTheoryPaper: (courseId: number, chapterId: number) =>
    request<TrainingCourseChapterTheoryPaperDetailModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`,
      method: 'GET',
    }),

  saveChapterTheoryPaper: (courseId: number, chapterId: number, data: TrainingCourseChapterTheoryPaperEditModel) =>
    request<TrainingCourseChapterTheoryPaperDetailModel, unknown>({
      path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),
}
