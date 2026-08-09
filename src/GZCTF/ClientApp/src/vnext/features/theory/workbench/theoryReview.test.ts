import { describe, expect, it } from 'vitest'
import { TheoryQuestionType } from '@Api'
import { buildTheoryReview, normalizeTheoryIndexes, theoryAnswersMatch } from './theoryReview'

const questions = [
  {
    id: 11,
    type: TheoryQuestionType.SingleChoice,
    title: 'single',
    options: ['A', 'B'],
    answerIndexes: [1],
  },
  {
    id: 12,
    type: TheoryQuestionType.MultipleChoice,
    title: 'multiple',
    options: ['A', 'B', 'C'],
    answerIndexes: [0, 2],
  },
  {
    id: 13,
    type: TheoryQuestionType.TrueFalse,
    title: 'empty',
    options: ['true', 'false'],
    answerIndexes: [0],
  },
]

describe('theory review', () => {
  it('normalizes duplicate and unordered answer indexes', () => {
    expect(normalizeTheoryIndexes([2, 0, 2])).toEqual([0, 2])
    expect(theoryAnswersMatch([2, 0], [0, 2])).toBe(true)
  })

  it('classifies correct, incorrect and unanswered questions', () => {
    const review = buildTheoryReview(questions, {
      11: [1],
      12: [0],
      13: [],
    })

    expect(review).toMatchObject({
      available: true,
      correctCount: 1,
      incorrectCount: 1,
      unansweredCount: 1,
      reviewCount: 2,
      accuracy: 33,
    })
    expect(review.items.map((item) => item.state)).toEqual(['correct', 'incorrect', 'unanswered'])
  })

  it('does not expose a partial review when correct answers are incomplete', () => {
    const review = buildTheoryReview([{ ...questions[0], answerIndexes: null }, questions[1]], {})
    expect(review.available).toBe(false)
  })
})
