import { ChallengeCategory, ChallengeType, EnvironmentType, FileType, NetworkMode } from '@Api'
import {
  TrainingCourseChallengeCreateModel,
  TrainingCourseDockerRegisterModel,
  TrainingCourseEditModel,
  TrainingCourseEnrollmentPolicy,
  TrainingCourseEnrollmentStatus,
  TrainingCourseImageTemplateModel,
  TrainingCourseLocalImageImportModel,
  TrainingCourseResourceEditModel,
  TrainingCourseResourceType,
  TrainingCourseTeacherRole,
} from '@Utils/TrainingApi'

export const emptyCourseDraft = (): TrainingCourseEditModel => ({
  title: '',
  slug: '',
  summary: '',
  description: '',
  coverFileHash: null,
  tags: [],
  enrollmentPolicy: TrainingCourseEnrollmentPolicy.TeacherApproval,
})

export const emptyResourceDraft = (): TrainingCourseResourceEditModel => ({
  title: '',
  description: '',
  type: TrainingCourseResourceType.File,
  externalUrl: null,
  localFileHash: null,
  order: 1,
  isVisible: true,
})

export const emptyChallengeDraft = (): TrainingCourseChallengeCreateModel => ({
  title: '',
  content: '',
  category: ChallengeCategory.Web,
  type: ChallengeType.StaticAttachment,
  environment: EnvironmentType.None,
  imageTemplateId: null,
  containerImage: '',
  memoryLimit: 128,
  cpuCount: 1,
  storageLimit: 256,
  exposePort: 80,
  networkMode: NetworkMode.Open,
  flagTemplate: 'flag{[TEAM_HASH]}',
  staticFlag: '',
  submissionLimit: 0,
  chapterId: null,
  order: 1,
  isRequired: true,
  displayTitle: null,
  attachmentType: FileType.None,
  attachmentFileHash: null,
  attachmentRemoteUrl: null,
})

export const emptyDockerRegisterDraft = (): TrainingCourseDockerRegisterModel => ({
  name: '',
  registryUrl: '',
  osType: '0',
  registryAuth: null,
})

export const emptyLocalImportDraft = (): TrainingCourseLocalImageImportModel => ({
  localPath: '',
  displayName: null,
})

export const normalizeKey = (value: string | number | undefined | null) => String(value ?? '').toLowerCase()

export const imageTypeText = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'docker') return 'Docker'
  if (key === '1' || key === 'qcow2') return 'QCOW2'
  if (key === '2' || key === 'ova') return 'OVA'
  if (key === '3' || key === 'vmdk') return 'VMDK'
  return String(value ?? '-')
}

export const osTypeText = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'linux') return 'Linux'
  if (key === '1' || key === 'windows') return 'Windows'
  return String(value ?? '-')
}

export const statusInfo = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'ready') return { label: '就绪', color: 'green' }
  if (key === '1' || key === 'importing') return { label: '导入中', color: 'violet' }
  if (key === '2' || key === 'error') return { label: '异常', color: 'red' }
  return { label: String(value ?? '未知'), color: 'gray' }
}

export const enrollmentStatusInfo = (status: TrainingCourseEnrollmentStatus) => {
  if (status === TrainingCourseEnrollmentStatus.Approved) return { label: '已加入', color: 'green' }
  if (status === TrainingCourseEnrollmentStatus.Pending) return { label: '待审核', color: 'yellow' }
  if (status === TrainingCourseEnrollmentStatus.Rejected) return { label: '已拒绝', color: 'red' }
  return { label: '已取消', color: 'gray' }
}

export const isDockerTemplate = (template: TrainingCourseImageTemplateModel) => imageTypeText(template.imageType) === 'Docker'
export const isWindowsTemplate = (template: TrainingCourseImageTemplateModel) => osTypeText(template.osType) === 'Windows'
export const isReadyTemplate = (template: TrainingCourseImageTemplateModel) => normalizeKey(template.status) === '0' || normalizeKey(template.status) === 'ready'

export const challengeCategoryOptions = Object.values(ChallengeCategory).map((value) => ({ value, label: value }))
export const challengeTypeOptions = Object.values(ChallengeType).map((value) => ({ value, label: value }))
export const environmentOptions = Object.values(EnvironmentType).map((value) => ({ value, label: value }))
export const networkModeOptions = Object.values(NetworkMode).map((value) => ({ value, label: value }))
export const courseTabValues = ['intro', 'chapters', 'resources', 'students', 'teachers', 'environments', 'challenges', 'theory-bank', 'homework']

export const teacherRoleOptions = [
  { value: TrainingCourseTeacherRole.Teacher, label: '授课教师' },
  { value: TrainingCourseTeacherRole.Owner, label: '负责人' },
]

export const formatSize = (bytes: number) => {
  if (!Number.isFinite(bytes) || bytes <= 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let index = 0
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024
    index += 1
  }
  return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

export const percentOf = (done: number, total: number) => (total > 0 ? Math.max(0, Math.min(100, Math.round((done / total) * 100))) : 0)

export const formatTime = (value?: number | string | null) => {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

export const teacherRoleText = (role: TrainingCourseTeacherRole | string) =>
  role === TrainingCourseTeacherRole.Owner ? '负责人' : '授课教师'

export const optionIndexesText = (options: string[], indexes: number[]) =>
  indexes.length
    ? indexes.map((index) => `${index + 1}. ${options[index] ?? `选项 ${index + 1}`}`).join('；')
    : '未作答'

export const theoryScoreText = (score: number | null | undefined, totalScore: number) => `${score ?? '--'}/${totalScore}`
