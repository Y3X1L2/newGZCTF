import {
  ChallengeCategory,
  ChallengeEditDetailModel,
  ChallengeType,
  ChallengeUpdateModel,
  EnvironmentType,
  NetworkMode,
} from '@Api'
import { fromLocalDateTimeInput, isContainerChallenge, toLocalDateTimeInput } from '../gamePresentation'

export interface ChallengeEditorDraft {
  title: string
  content: string
  category: ChallengeCategory
  hintsText: string
  flagTemplate: string
  isEnabled: boolean
  fileName: string
  deadline: string
  submissionLimit: number
  containerImage: string
  memoryLimit: number
  cpuCount: number
  storageLimit: number
  exposePort: number
  networkMode: NetworkMode
  enableTrafficCapture: boolean
  disableBloodBonus: boolean
  originalScore: number
  minScoreRate: number
  difficulty: number
  environment: EnvironmentType
  imageTemplateId: number | null
}

export function challengeEditorDraft(challenge: ChallengeEditDetailModel): ChallengeEditorDraft {
  return {
    title: challenge.title,
    content: challenge.content ?? '',
    category: challenge.category,
    hintsText: (challenge.hints ?? []).join('\n'),
    flagTemplate: challenge.flagTemplate ?? '',
    isEnabled: challenge.isEnabled,
    fileName: challenge.fileName ?? '',
    deadline: challenge.deadlineUtc ? toLocalDateTimeInput(challenge.deadlineUtc) : '',
    submissionLimit: challenge.submissionLimit ?? 0,
    containerImage: challenge.containerImage ?? '',
    memoryLimit: challenge.memoryLimit ?? 128,
    cpuCount: challenge.cpuCount ?? 1,
    storageLimit: challenge.storageLimit ?? 256,
    exposePort: challenge.exposePort ?? 80,
    networkMode: challenge.networkMode ?? NetworkMode.Open,
    enableTrafficCapture: challenge.enableTrafficCapture ?? false,
    disableBloodBonus: challenge.disableBloodBonus ?? false,
    originalScore: challenge.originalScore ?? 1000,
    minScoreRate: challenge.minScoreRate ?? 0.25,
    difficulty: challenge.difficulty ?? 5,
    environment: challenge.environment ?? (isContainerChallenge(challenge.type) ? EnvironmentType.Docker : EnvironmentType.None),
    imageTemplateId: challenge.imageTemplateId ?? null,
  }
}

export function validateChallengeEditorDraft(draft: ChallengeEditorDraft, type: ChallengeType) {
  const issues: string[] = []
  if (!draft.title.trim()) issues.push('请输入题目名称。')
  if (draft.submissionLimit < 0) issues.push('提交次数限制不能小于 0。')
  if (draft.originalScore <= 0) issues.push('初始分值必须大于 0。')
  if (draft.minScoreRate < 0 || draft.minScoreRate > 1) issues.push('最低得分率必须在 0 到 1 之间。')
  if (draft.difficulty <= 0) issues.push('难度系数必须大于 0。')
  if (isContainerChallenge(type)) {
    if (draft.environment === EnvironmentType.Docker) {
      if (!draft.containerImage.trim()) issues.push('请选择 Docker 模板或填写完整镜像引用。')
      if (draft.exposePort < 1 || draft.exposePort > 65535) issues.push('Docker 暴露端口必须在 1 到 65535 之间。')
    } else if (draft.environment === EnvironmentType.WindowsVM && !draft.imageTemplateId) {
      issues.push('请选择已就绪的 Windows 镜像模板。')
    } else if (draft.environment === EnvironmentType.None) {
      issues.push('容器题必须配置运行环境。')
    }
  }
  if (type === ChallengeType.DynamicContainer && !draft.flagTemplate.trim()) {
    issues.push('动态容器题必须配置 Flag 模板。')
  }
  return issues
}

export function challengeUpdatePayload(draft: ChallengeEditorDraft, type: ChallengeType): ChallengeUpdateModel {
  const container = isContainerChallenge(type)
  const environment = container ? draft.environment : EnvironmentType.None
  return {
    title: draft.title.trim(),
    content: draft.content,
    category: draft.category,
    hints: draft.hintsText.split('\n').map((hint) => hint.trim()).filter(Boolean),
    flagTemplate: type === ChallengeType.DynamicContainer ? draft.flagTemplate.trim() || null : null,
    isEnabled: draft.isEnabled,
    fileName: type === ChallengeType.DynamicAttachment ? draft.fileName.trim() || null : null,
    deadlineUtc: draft.deadline ? fromLocalDateTimeInput(draft.deadline) : null,
    submissionLimit: Math.max(0, draft.submissionLimit),
    containerImage: environment === EnvironmentType.Docker ? draft.containerImage.trim() : null,
    memoryLimit: container ? Math.max(32, draft.memoryLimit) : null,
    cpuCount: container ? Math.max(1, draft.cpuCount) : null,
    storageLimit: container ? Math.max(0, draft.storageLimit) : null,
    exposePort: environment === EnvironmentType.Docker ? draft.exposePort : null,
    networkMode: container ? draft.networkMode : null,
    enableTrafficCapture: container ? draft.enableTrafficCapture : false,
    disableBloodBonus: draft.disableBloodBonus,
    originalScore: Math.max(1, draft.originalScore),
    minScoreRate: draft.minScoreRate,
    difficulty: draft.difficulty,
    environment,
    imageTemplateId: environment === EnvironmentType.WindowsVM ? draft.imageTemplateId : null,
  }
}
