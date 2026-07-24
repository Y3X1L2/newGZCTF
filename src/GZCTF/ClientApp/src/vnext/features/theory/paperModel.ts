import {
  TheoryPaperDetailModel,
  TheoryPaperEditModel,
  TheoryPaperQuestionEditModel,
  TheoryQuestionBankItemModel,
} from '@Api'
import { normalizeTheoryQuestion, validateTheoryQuestion } from './questionModel'

export function theoryPaperQuestionFromBank(
  question: TheoryQuestionBankItemModel,
  score = 1,
  order = 1
): TheoryPaperQuestionEditModel {
  const normalized = normalizeTheoryQuestion(question)
  return {
    ...normalized,
    sourceQuestionId: question.id ?? null,
    score: Math.max(1, Math.floor(score)),
    order,
  }
}

export function normalizedTheoryPaper(paper: TheoryPaperEditModel): TheoryPaperEditModel {
  return {
    title: paper.title.trim(),
    description: paper.description?.trim() ?? '',
    questions: [...(paper.questions ?? [])]
      .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
      .map((question, index) => ({
        ...normalizeTheoryQuestion(question),
        id: question.id,
        sourceQuestionId: question.sourceQuestionId ?? null,
        score: Math.max(1, Math.floor(question.score ?? 1)),
        order: index + 1,
      })),
  }
}

export function theoryPaperTotalScore(paper: Pick<TheoryPaperEditModel, 'questions'>) {
  return (paper.questions ?? []).reduce((total, question) => total + Math.max(0, question.score ?? 0), 0)
}

export function reorderTheoryPaperQuestions(
  questions: TheoryPaperQuestionEditModel[],
  index: number,
  direction: -1 | 1
) {
  const target = index + direction
  if (index < 0 || index >= questions.length || target < 0 || target >= questions.length) return questions
  const reordered = [...questions]
  ;[reordered[index], reordered[target]] = [reordered[target], reordered[index]]
  return reordered.map((question, order) => ({ ...question, order: order + 1 }))
}

export function validateTheoryPaper(paper: TheoryPaperEditModel) {
  const normalized = normalizedTheoryPaper(paper)
  const questions = normalized.questions ?? []
  const issues: string[] = []
  if (!normalized.title) issues.push('请输入试卷名称。')
  if (!questions.length) issues.push('试卷至少需要一道题目。')
  const sourceIds = questions
    .map((question) => question.sourceQuestionId)
    .filter((id): id is number => Boolean(id))
  if (new Set(sourceIds).size !== sourceIds.length) issues.push('同一道题库题目不能重复加入试卷。')
  questions.forEach((question, index) => {
    const questionIssues = validateTheoryQuestion(question)
    if ((question.score ?? 0) <= 0) questionIssues.push('分值必须大于 0。')
    if (questionIssues.length) issues.push(`第 ${index + 1} 题：${questionIssues.join(' ')}`)
  })
  return issues
}

function secureRandom() {
  const value = crypto.getRandomValues(new Uint32Array(1))[0]
  return value / 0x1_0000_0000
}

export function selectRandomTheoryQuestions<T>(
  candidates: T[],
  count: number,
  random: () => number = secureRandom
) {
  const shuffled = [...candidates]
  for (let index = shuffled.length - 1; index > 0; index -= 1) {
    const target = Math.floor(random() * (index + 1))
    ;[shuffled[index], shuffled[target]] = [shuffled[target], shuffled[index]]
  }
  return shuffled.slice(0, Math.max(0, Math.min(shuffled.length, Math.floor(count))))
}

export function theoryPaperDraft(paper: TheoryPaperDetailModel): TheoryPaperEditModel {
  return normalizedTheoryPaper({
    title: paper.title,
    description: paper.description,
    questions: paper.questions,
  })
}
