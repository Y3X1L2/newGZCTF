import {
  TheoryQuestionBankItemModel,
  TheoryQuestionEditModel,
  TheoryQuestionType,
} from '@Api'

export const DEFAULT_THEORY_BANK = 'Default'

export type TheoryImportDuplicateStrategy = 'copy' | 'overwrite' | 'skip'
export type TheoryImportAction = 'create' | 'skip' | 'update'

export interface TheoryQuestionImportIssue {
  index: number | null
  message: string
}

export interface TheoryQuestionImportInspection {
  questions: TheoryQuestionEditModel[]
  issues: TheoryQuestionImportIssue[]
}

export interface TheoryQuestionImportPlanItem {
  index: number
  question: TheoryQuestionEditModel
  action: TheoryImportAction
  existingId?: number
}

export interface TheoryQuestionImportPlan {
  items: TheoryQuestionImportPlanItem[]
  createCount: number
  updateCount: number
  skipCount: number
}

export function emptyTheoryQuestion(): TheoryQuestionEditModel {
  return {
    type: TheoryQuestionType.SingleChoice,
    bankName: DEFAULT_THEORY_BANK,
    title: '',
    content: '',
    options: ['选项 A', '选项 B'],
    answerIndexes: [0],
    tags: [],
  }
}

function normalizedIndexes(indexes: number[], optionCount: number, multiple: boolean) {
  const values = [...new Set(indexes.filter((index) => Number.isInteger(index) && index >= 0 && index < optionCount))]
  return multiple ? values.sort((left, right) => left - right) : values.slice(0, 1)
}

export function normalizeTheoryQuestion(question: TheoryQuestionEditModel): TheoryQuestionEditModel {
  const type = question.type ?? TheoryQuestionType.SingleChoice
  const suppliedOptions = question.options ?? []
  const options =
    type === TheoryQuestionType.TrueFalse
      ? ['正确', '错误']
      : suppliedOptions.map((option) => option.trim()).filter(Boolean)
  const safeOptions = options.length >= 2 ? options : ['选项 A', '选项 B']
  return {
    type,
    bankName: question.bankName?.trim().slice(0, 128) || DEFAULT_THEORY_BANK,
    title: question.title.trim(),
    content: question.content?.trim() || '',
    options: safeOptions,
    answerIndexes: normalizedIndexes(
      question.answerIndexes ?? [],
      safeOptions.length,
      type === TheoryQuestionType.MultipleChoice
    ),
    tags: [...new Set((question.tags ?? []).map((tag) => tag.trim()).filter(Boolean))],
  }
}

export function validateTheoryQuestion(question: TheoryQuestionEditModel) {
  const issues: string[] = []
  const normalized = normalizeTheoryQuestion(question)
  if (!normalized.title) issues.push('题干不能为空。')
  if ((normalized.bankName ?? '').length > 128) issues.push('题库名称不能超过 128 个字符。')
  if ((normalized.options ?? []).length < 2) issues.push('选择题至少需要两个选项。')
  if (!(normalized.answerIndexes ?? []).length) issues.push('必须配置正确答案。')
  if ((normalized.answerIndexes ?? []).some((index) => index < 0 || index >= (normalized.options ?? []).length)) {
    issues.push('正确答案索引超出选项范围。')
  }
  if (
    normalized.type !== TheoryQuestionType.MultipleChoice &&
    (normalized.answerIndexes ?? []).length !== 1
  ) {
    issues.push('单选题和判断题必须且只能有一个正确答案。')
  }
  return issues
}

function parseType(value: unknown) {
  if (value === undefined || value === null || value === '') return TheoryQuestionType.SingleChoice
  if (Object.values(TheoryQuestionType).includes(value as TheoryQuestionType)) return value as TheoryQuestionType
  const text = String(value).trim().toLocaleLowerCase('zh-CN')
  if (text.includes('multiple') || text.includes('multi') || text.includes('多选')) {
    return TheoryQuestionType.MultipleChoice
  }
  if (text.includes('true') || text.includes('false') || text.includes('judge') || text.includes('判断')) {
    return TheoryQuestionType.TrueFalse
  }
  if (text.includes('single') || text.includes('单选')) return TheoryQuestionType.SingleChoice
  return null
}

function parseAnswerIndexes(value: unknown, options: string[]) {
  const parseOne = (entry: unknown): number[] => {
    if (typeof entry === 'number') return [entry]
    if (typeof entry === 'boolean') return [entry ? 0 : 1]
    const text = String(entry ?? '').trim()
    if (!text) return []
    const exact = options.findIndex((option) => option === text)
    if (exact >= 0) return [exact]
    if (/^[A-Z]$/i.test(text)) return [text.toUpperCase().charCodeAt(0) - 65]
    const numeric = Number(text)
    return Number.isInteger(numeric) ? [numeric] : []
  }

  if (Array.isArray(value)) return value.flatMap(parseOne)
  if (typeof value === 'string' && /[,，;；、\s]/.test(value)) return value.split(/[,，;；、\s]+/).flatMap(parseOne)
  return parseOne(value)
}

function rawQuestions(value: unknown) {
  if (Array.isArray(value)) return value
  if (value && typeof value === 'object' && Array.isArray((value as Record<string, unknown>).questions)) {
    return (value as Record<string, unknown>).questions as unknown[]
  }
  return null
}

function parseTags(value: unknown) {
  if (Array.isArray(value)) return value.map(String)
  if (typeof value === 'string') return value.split(/[,，、]/)
  return []
}

function parseRawQuestion(raw: unknown, defaultBank: string) {
  if (!raw || typeof raw !== 'object') throw new Error('题目不是有效对象。')
  const item = raw as Record<string, unknown>
  const type = parseType(item.type ?? item.questionType)
  if (!type) throw new Error(`无法识别题型“${String(item.type ?? item.questionType)}”。`)
  const title = String(item.title ?? item.question ?? item.stem ?? '').trim()
  if (!title) throw new Error('缺少 title/question/stem。')

  const options =
    type === TheoryQuestionType.TrueFalse
      ? ['正确', '错误']
      : Array.isArray(item.options ?? item.choices)
        ? ((item.options ?? item.choices) as unknown[]).map(String).map((option) => option.trim()).filter(Boolean)
        : []
  if (type !== TheoryQuestionType.TrueFalse && options.length < 2) throw new Error('选择题至少需要两个有效选项。')

  const answerValue =
    item.answerIndexes ?? item.answerIndex ?? item.correctIndexes ?? item.correctIndex ?? item.answer
  const indexes = parseAnswerIndexes(answerValue, options)
  if (!indexes.length) throw new Error('缺少可识别的正确答案。')
  if (indexes.some((answerIndex) => answerIndex < 0 || answerIndex >= options.length)) {
    throw new Error('正确答案索引超出选项范围。')
  }
  if (type !== TheoryQuestionType.MultipleChoice && new Set(indexes).size !== 1) {
    throw new Error('单选题和判断题只能有一个正确答案。')
  }

  const question = normalizeTheoryQuestion({
    type,
    bankName: String(item.bankName ?? defaultBank ?? DEFAULT_THEORY_BANK),
    title,
    content: String(item.content ?? item.description ?? item.analysis ?? ''),
    options,
    answerIndexes: indexes,
    tags: parseTags(item.tags),
  })
  const issues = validateTheoryQuestion(question)
  if (issues.length) throw new Error(issues.join(' '))
  return question
}

export function inspectTheoryQuestionJson(text: string, defaultBank = DEFAULT_THEORY_BANK): TheoryQuestionImportInspection {
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return { questions: [], issues: [{ index: null, message: 'JSON 语法无效。' }] }
  }

  const source = rawQuestions(parsed)
  if (!source) return { questions: [], issues: [{ index: null, message: 'JSON 必须是题目数组，或包含 questions 数组。' }] }
  if (!source.length) return { questions: [], issues: [{ index: null, message: 'JSON 题库中没有题目。' }] }

  const questions: TheoryQuestionEditModel[] = []
  const issues: TheoryQuestionImportIssue[] = []
  source.forEach((raw, index) => {
    try {
      questions.push(parseRawQuestion(raw, defaultBank))
    } catch (error) {
      issues.push({ index, message: error instanceof Error ? error.message : '题目格式无效。' })
    }
  })
  return { questions, issues }
}

export function parseTheoryQuestionJson(text: string, defaultBank = DEFAULT_THEORY_BANK) {
  const result = inspectTheoryQuestionJson(text, defaultBank)
  if (result.issues.length) {
    const first = result.issues[0]
    throw new Error(`${first.index === null ? '' : `第 ${first.index + 1} 题：`}${first.message}`)
  }
  return result.questions
}

function normalizedIdentityText(value: string | null | undefined) {
  return (value ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase('zh-CN')
}

export function theoryQuestionIdentity(question: TheoryQuestionEditModel) {
  return [
    normalizedIdentityText(question.bankName || DEFAULT_THEORY_BANK),
    question.type,
    normalizedIdentityText(question.title),
  ].join('::')
}

export function buildTheoryQuestionImportPlan(
  questions: TheoryQuestionEditModel[],
  existing: TheoryQuestionBankItemModel[],
  strategy: TheoryImportDuplicateStrategy
): TheoryQuestionImportPlan {
  const existingByIdentity = new Map(
    existing.filter((question) => question.id).map((question) => [theoryQuestionIdentity(question), question.id as number])
  )
  const newIdentities = new Set<string>()
  const items = questions.map((question, index): TheoryQuestionImportPlanItem => {
    const identity = theoryQuestionIdentity(question)
    const existingId = existingByIdentity.get(identity)
    if (strategy === 'copy') return { index, question, action: 'create' }
    if (existingId) {
      return strategy === 'overwrite'
        ? { index, question, action: 'update', existingId }
        : { index, question, action: 'skip', existingId }
    }
    if (newIdentities.has(identity)) return { index, question, action: 'skip' }
    newIdentities.add(identity)
    return { index, question, action: 'create' }
  })
  return {
    items,
    createCount: items.filter((item) => item.action === 'create').length,
    updateCount: items.filter((item) => item.action === 'update').length,
    skipCount: items.filter((item) => item.action === 'skip').length,
  }
}

export function theoryQuestionTypeLabel(type?: TheoryQuestionType) {
  if (type === TheoryQuestionType.MultipleChoice) return '多选题'
  if (type === TheoryQuestionType.TrueFalse) return '判断题'
  return '单选题'
}

export function theoryAnswerLabel(question: TheoryQuestionEditModel) {
  return (question.answerIndexes ?? [])
    .map((index) =>
      question.type === TheoryQuestionType.TrueFalse
        ? index === 0 ? '正确' : index === 1 ? '错误' : undefined
        : question.options?.[index]
    )
    .filter(Boolean)
    .join('、')
}
