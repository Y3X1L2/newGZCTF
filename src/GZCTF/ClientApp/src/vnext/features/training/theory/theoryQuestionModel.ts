import { TheoryQuestionEditModel, TheoryQuestionType } from '@Api'

export const DEFAULT_COURSE_BANK = 'Default'

export function emptyTheoryQuestion(): TheoryQuestionEditModel {
  return {
    type: TheoryQuestionType.SingleChoice,
    bankName: DEFAULT_COURSE_BANK,
    title: '',
    content: '',
    options: ['选项 A', '选项 B'],
    answerIndexes: [0],
    tags: [],
  }
}

function uniqueIndexes(indexes: number[], optionCount: number, multiple: boolean) {
  const values = [...new Set(indexes.filter((index) => Number.isInteger(index) && index >= 0 && index < optionCount))]
  if (!values.length) return [0]
  return multiple ? values.sort((left, right) => left - right) : [values[0]]
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
    bankName: question.bankName?.trim().slice(0, 128) || DEFAULT_COURSE_BANK,
    title: question.title.trim(),
    content: question.content?.trim() || '',
    options: safeOptions,
    answerIndexes: uniqueIndexes(
      question.answerIndexes ?? [],
      safeOptions.length,
      type === TheoryQuestionType.MultipleChoice
    ),
    tags: [...new Set((question.tags ?? []).map((tag) => tag.trim()).filter(Boolean))],
  }
}

function normalizeType(value: unknown) {
  const text = String(value ?? '').toLocaleLowerCase('zh-CN')
  if (text.includes('multiple') || text.includes('multi') || text.includes('多选')) {
    return TheoryQuestionType.MultipleChoice
  }
  if (text.includes('true') || text.includes('false') || text.includes('judge') || text.includes('判断')) {
    return TheoryQuestionType.TrueFalse
  }
  return TheoryQuestionType.SingleChoice
}

function answerIndexes(value: unknown, options: string[]) {
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

export function parseTheoryQuestionJson(text: string, defaultBank: string) {
  const parsed = JSON.parse(text) as unknown
  const source = Array.isArray(parsed)
    ? parsed
    : parsed && typeof parsed === 'object' && Array.isArray((parsed as Record<string, unknown>).questions)
      ? ((parsed as Record<string, unknown>).questions as unknown[])
      : null
  if (!source) throw new Error('JSON 中必须包含 questions 数组，或直接使用题目数组。')
  if (!source.length) throw new Error('JSON 题库中没有题目。')

  return source.map((raw, index) => {
    if (!raw || typeof raw !== 'object') throw new Error(`第 ${index + 1} 项不是有效题目对象。`)
    const item = raw as Record<string, unknown>
    const type = normalizeType(item.type ?? item.questionType)
    const options =
      type === TheoryQuestionType.TrueFalse
        ? ['正确', '错误']
        : ((item.options ?? item.choices ?? []) as unknown[])
            .map(String)
            .map((option) => option.trim())
            .filter(Boolean)
    const safeOptions = options.length >= 2 ? options : ['选项 A', '选项 B']
    const title = String(item.title ?? item.question ?? item.stem ?? '').trim()
    if (!title) throw new Error(`第 ${index + 1} 道题缺少 title/question/stem。`)

    return normalizeTheoryQuestion({
      type,
      bankName: String(item.bankName ?? defaultBank ?? DEFAULT_COURSE_BANK),
      title,
      content: String(item.content ?? item.description ?? item.analysis ?? ''),
      options: safeOptions,
      answerIndexes: answerIndexes(
        item.answerIndexes ?? item.answerIndex ?? item.correctIndexes ?? item.correctIndex ?? item.answer,
        safeOptions
      ),
      tags: Array.isArray(item.tags) ? item.tags.map(String) : [],
    })
  })
}

export function theoryQuestionTypeLabel(type?: TheoryQuestionType) {
  if (type === TheoryQuestionType.MultipleChoice) return '多选题'
  if (type === TheoryQuestionType.TrueFalse) return '判断题'
  return '单选题'
}

export function theoryAnswerLabel(question: TheoryQuestionEditModel) {
  return (question.answerIndexes ?? [])
    .map((index) => question.options?.[index])
    .filter(Boolean)
    .join('、')
}
