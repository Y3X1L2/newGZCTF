import { TheoryPlayerQuestionModel } from '@Api'

export type TheoryReviewState = 'correct' | 'incorrect' | 'unanswered'

export interface ReviewableTheoryQuestion extends TheoryPlayerQuestionModel {
  answerIndexes?: number[] | null
}

export interface TheoryReviewItem {
  question: ReviewableTheoryQuestion
  questionIndex: number
  selectedIndexes: number[]
  correctIndexes: number[]
  state: TheoryReviewState
}

export interface TheoryReviewSummary {
  available: boolean
  items: TheoryReviewItem[]
  correctCount: number
  incorrectCount: number
  unansweredCount: number
  reviewCount: number
  accuracy: number
}

export function normalizeTheoryIndexes(indexes: number[] | null | undefined) {
  return [...new Set(indexes ?? [])].sort((left, right) => left - right)
}

export function theoryAnswersMatch(selected: number[], correct: number[]) {
  const normalizedSelected = normalizeTheoryIndexes(selected)
  const normalizedCorrect = normalizeTheoryIndexes(correct)
  return (
    normalizedSelected.length === normalizedCorrect.length &&
    normalizedSelected.every((value, index) => value === normalizedCorrect[index])
  )
}

export function buildTheoryReview(
  questions: ReviewableTheoryQuestion[],
  answers: Record<number, number[]>
): TheoryReviewSummary {
  const items = questions.flatMap<TheoryReviewItem>((question, questionIndex) => {
    if (question.id === undefined || !Array.isArray(question.answerIndexes)) return []

    const selectedIndexes = normalizeTheoryIndexes(answers[question.id])
    const correctIndexes = normalizeTheoryIndexes(question.answerIndexes)
    const state: TheoryReviewState =
      selectedIndexes.length === 0
        ? 'unanswered'
        : theoryAnswersMatch(selectedIndexes, correctIndexes)
          ? 'correct'
          : 'incorrect'

    return [{ question, questionIndex, selectedIndexes, correctIndexes, state }]
  })

  const available = questions.length > 0 && items.length === questions.length
  const correctCount = items.filter((item) => item.state === 'correct').length
  const incorrectCount = items.filter((item) => item.state === 'incorrect').length
  const unansweredCount = items.filter((item) => item.state === 'unanswered').length
  const reviewCount = incorrectCount + unansweredCount
  const accuracy = items.length ? Math.round((correctCount / items.length) * 100) : 0

  return {
    available,
    items,
    correctCount,
    incorrectCount,
    unansweredCount,
    reviewCount,
    accuracy,
  }
}
