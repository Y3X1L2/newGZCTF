import {
  ChallengeCategory,
  ChallengeEditDetailModel,
  ChallengeInfoModel,
  ChallengeType,
  EnvironmentType,
  GameInfoModel,
  GameType,
} from '@Api'
import { runtimeTemplateAvailable } from '../../challenge-runtime/imageTemplateCapabilities'
import type { ImageTemplateSummary } from '../api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export type GameLifecycle = 'scheduled' | 'running' | 'ended'

export interface GameCreateDraft {
  title: string
  summary: string
  gameType: GameType
  hidden: boolean
  practiceMode: boolean
  isTest: boolean
  acceptWithoutReview: boolean
  inviteCode: string
  teamMemberCountLimit: number
  containerCountLimit: number
  start: string
  end: string
}

export function gameTypeLabel(type?: GameType) {
  if (type === GameType.AWDP) return 'AWDP'
  if (type === GameType.Theory) return '理论考试'
  if (type === GameType.Penetration) return '渗透演练'
  if (type === GameType.Mixed) return '混合赛制'
  return 'CTF Jeopardy'
}

export function gameLifecycle(game: Pick<GameInfoModel, 'start' | 'end'>, now = Date.now()): GameLifecycle {
  if (game.start > now) return 'scheduled'
  if (game.end <= now) return 'ended'
  return 'running'
}

export function gameLifecycleMeta(lifecycle: GameLifecycle) {
  if (lifecycle === 'running') return { label: '进行中', tone: 'success' as AdminStatusTone }
  if (lifecycle === 'scheduled') return { label: '未开始', tone: 'info' as AdminStatusTone }
  return { label: '已结束', tone: 'neutral' as AdminStatusTone }
}

export function toLocalDateTimeInput(timestamp: number) {
  if (!Number.isFinite(timestamp)) return ''
  const date = new Date(timestamp)
  if (!Number.isFinite(date.getTime())) return ''
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function fromLocalDateTimeInput(value: string) {
  return new Date(value).getTime()
}

export function emptyGameCreateDraft(now = Date.now()): GameCreateDraft {
  return {
    title: '',
    summary: '',
    gameType: GameType.Jeopardy,
    hidden: true,
    practiceMode: false,
    isTest: false,
    acceptWithoutReview: false,
    inviteCode: '',
    teamMemberCountLimit: 0,
    containerCountLimit: 3,
    start: toLocalDateTimeInput(now + 60 * 60_000),
    end: toLocalDateTimeInput(now + 3 * 60 * 60_000),
  }
}

export function validateGameCreateDraft(draft: GameCreateDraft) {
  const issues: string[] = []
  if (!draft.title.trim()) issues.push('请输入比赛名称。')
  const start = fromLocalDateTimeInput(draft.start)
  const end = fromLocalDateTimeInput(draft.end)
  if (!Number.isFinite(start) || !Number.isFinite(end)) issues.push('请输入有效的比赛时间。')
  else if (end <= start) issues.push('比赛结束时间必须晚于开始时间。')
  if (draft.inviteCode.trim().length > 32) issues.push('比赛邀请码不能超过 32 个字符。')
  if (draft.teamMemberCountLimit < 0) issues.push('队伍人数限制不能小于 0。')
  if (draft.containerCountLimit < 0) issues.push('实例数量限制不能小于 0。')
  return issues
}

export function gameCreatePayload(draft: GameCreateDraft): GameInfoModel {
  return {
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    content: '',
    gameType: draft.gameType,
    hidden: draft.hidden,
    practiceMode: draft.practiceMode,
    isTest: draft.isTest,
    acceptWithoutReview: draft.acceptWithoutReview,
    inviteCode: draft.inviteCode.trim() || null,
    teamMemberCountLimit: Math.max(0, draft.teamMemberCountLimit),
    containerCountLimit: Math.max(0, draft.containerCountLimit),
    start: fromLocalDateTimeInput(draft.start),
    end: fromLocalDateTimeInput(draft.end),
    writeupRequired: false,
    writeupDeadline: fromLocalDateTimeInput(draft.end),
    writeupNote: '',
  }
}

export function validateGameImportFile(file: Pick<File, 'name' | 'size' | 'type'> | null) {
  if (!file) return ['请选择比赛 ZIP 包。']
  const issues: string[] = []
  if (!file.name.toLocaleLowerCase('en-US').endsWith('.zip')) issues.push('比赛导入文件必须使用 .zip 扩展名。')
  if (!['application/zip', 'application/x-zip-compressed'].includes(file.type)) {
    issues.push('比赛导入文件的 MIME 类型不是 ZIP。')
  }
  if (file.size <= 0) issues.push('比赛导入文件不能为空。')
  if (file.size > 512 * 1024 * 1024) issues.push('比赛导入文件不能超过 512 MB。')
  return issues
}

export function challengeTypeLabel(type?: ChallengeType) {
  if (type === ChallengeType.DynamicContainer) return '动态容器'
  if (type === ChallengeType.StaticContainer) return '静态容器'
  if (type === ChallengeType.DynamicAttachment) return '动态附件'
  return '静态附件'
}

export function challengeCategoryLabel(category?: ChallengeCategory) {
  return category ?? ChallengeCategory.Misc
}

export function challengeEnvironmentLabel(environment?: EnvironmentType | null) {
  if (environment === EnvironmentType.Docker) return 'Docker'
  if (environment === EnvironmentType.WindowsVM) return 'Windows VM'
  return '无环境'
}

export function isContainerChallenge(type?: ChallengeType) {
  return type === ChallengeType.DynamicContainer || type === ChallengeType.StaticContainer
}

export function challengeConfigurationIssues(challenge: ChallengeInfoModel | ChallengeEditDetailModel) {
  const issues: string[] = []
  if (!challenge.title.trim()) issues.push('缺少题目名称')
  if (isContainerChallenge(challenge.type)) {
    if (challenge.environment === EnvironmentType.Docker) {
      if (!challenge.containerImage?.trim()) issues.push('缺少 Docker 镜像')
      if (!challenge.exposePort) issues.push('缺少暴露端口')
    } else if (challenge.environment === EnvironmentType.WindowsVM) {
      if (!challenge.imageTemplateId) issues.push('缺少 Windows 镜像模板')
    } else {
      issues.push('缺少运行环境')
    }
  }
  if ('flags' in challenge && challenge.type !== ChallengeType.DynamicContainer && challenge.flags.length === 0) {
    issues.push('缺少 Flag')
  }
  if ('flagTemplate' in challenge && challenge.type === ChallengeType.DynamicContainer && !challenge.flagTemplate?.trim()) {
    issues.push('缺少动态 Flag 模板')
  }
  if ('attachment' in challenge && challenge.type === ChallengeType.StaticAttachment && !challenge.attachment) {
    issues.push('缺少题目附件')
  }
  return issues
}

export function templateAvailableForEnvironment(template: ImageTemplateSummary, environment: EnvironmentType) {
  return runtimeTemplateAvailable(template, environment)
}
