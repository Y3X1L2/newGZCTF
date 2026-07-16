import {
  AnswerType,
  FileType,
  FlagCreateModel,
  FlagInfoModel,
  FlagScoreMode,
} from '@Api'

export interface FlagEditorDraft {
  flag: string
  orderIndex: number
  description: string
  scoreMode: FlagScoreMode
  fixedScore: number
  maxAttempts: number
  answerType: AnswerType
  customName: string
  attachmentHash: string
  attachmentType: FileType
  remoteUrl: string
}

export interface FlagValidationOptions {
  dynamicAttachment: boolean
  existingAttachment: boolean
  hasLocalFile: boolean
}

export function emptyFlagEditorDraft(orderIndex = 0): FlagEditorDraft {
  return {
    flag: '',
    orderIndex,
    description: '',
    scoreMode: FlagScoreMode.InheritDecay,
    fixedScore: 0,
    maxAttempts: 0,
    answerType: AnswerType.Flag,
    customName: '',
    attachmentHash: '',
    attachmentType: FileType.None,
    remoteUrl: '',
  }
}

export function flagEditorDraft(flag: FlagInfoModel): FlagEditorDraft {
  return {
    flag: flag.flag ?? '',
    orderIndex: flag.orderIndex ?? 0,
    description: flag.description ?? '',
    scoreMode: flag.scoreMode ?? FlagScoreMode.InheritDecay,
    fixedScore: flag.fixedScore ?? 0,
    maxAttempts: flag.maxAttempts ?? 0,
    answerType: flag.answerType ?? AnswerType.Flag,
    customName: flag.customName ?? '',
    attachmentHash: flag.attachmentHash ?? '',
    attachmentType: FileType.None,
    remoteUrl: '',
  }
}

function isHttpUrl(value: string) {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

export function validateFlagEditorDraft(draft: FlagEditorDraft, options: FlagValidationOptions) {
  const issues: string[] = []
  if (!draft.flag.trim()) issues.push('请输入 Flag 或判定值。')
  if (draft.flag.trim().length > 127) issues.push('Flag 或判定值不能超过 127 个字符。')
  if (draft.orderIndex < 0) issues.push('显示顺序不能小于 0。')
  if (draft.description.trim().length > 512) issues.push('Flag 描述不能超过 512 个字符。')
  if (draft.customName.trim().length > 64) issues.push('显示名称不能超过 64 个字符。')
  if (draft.maxAttempts < 0) issues.push('尝试次数不能小于 0。')
  if (draft.scoreMode === FlagScoreMode.FixedScore && draft.fixedScore <= 0) {
    issues.push('固定分值必须大于 0。')
  }
  if (draft.answerType === AnswerType.File && !/^[a-f\d]{64}$/i.test(draft.attachmentHash.trim())) {
    issues.push('文件答案必须配置 64 位 SHA256。')
  }
  if (options.dynamicAttachment && !options.existingAttachment) {
    if (draft.attachmentType === FileType.None) issues.push('动态附件 Flag 必须绑定本地文件或外部链接。')
    if (draft.attachmentType === FileType.Local && !options.hasLocalFile) issues.push('请选择需要绑定的本地附件。')
    if (draft.attachmentType === FileType.Remote && !isHttpUrl(draft.remoteUrl.trim())) {
      issues.push('请输入有效的 HTTP 或 HTTPS 附件地址。')
    }
  }
  return issues
}

export function flagCreatePayload(draft: FlagEditorDraft, fileHash?: string | null): FlagCreateModel {
  return {
    flag: draft.flag.trim(),
    orderIndex: Math.max(0, draft.orderIndex),
    description: draft.description.trim() || null,
    scoreMode: draft.scoreMode,
    fixedScore: draft.scoreMode === FlagScoreMode.FixedScore ? Math.max(1, draft.fixedScore) : 0,
    maxAttempts: Math.max(0, draft.maxAttempts),
    answerType: draft.answerType,
    customName: draft.customName.trim() || null,
    attachmentHash: draft.answerType === AnswerType.File ? draft.attachmentHash.trim().toLowerCase() : null,
    attachmentType: draft.attachmentType,
    fileHash: draft.attachmentType === FileType.Local ? fileHash ?? null : null,
    remoteUrl: draft.attachmentType === FileType.Remote ? draft.remoteUrl.trim() : null,
  }
}
