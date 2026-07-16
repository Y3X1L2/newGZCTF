import { describe, expect, it } from 'vitest'
import { TheoryQuestionType } from '@Api'
import {
  normalizedTheoryPaper,
  reorderTheoryPaperQuestions,
  selectRandomTheoryQuestions,
  theoryPaperQuestionFromBank,
  theoryPaperTotalScore,
  validateTheoryPaper,
} from './paperModel'

const bankQuestion = {
  id: 12,
  type: TheoryQuestionType.SingleChoice,
  bankName: 'Web',
  title: 'HTTP 方法',
  content: '',
  options: ['GET', 'SSH'],
  answerIndexes: [0],
  tags: ['HTTP'],
}

describe('theory paper model', () => {
  it('creates an isolated paper snapshot and totals scores', () => {
    const question = theoryPaperQuestionFromBank(bankQuestion, 5, 3)
    const paper = normalizedTheoryPaper({ title: ' 测试卷 ', description: '', questions: [question] })
    expect(paper.title).toBe('测试卷')
    expect(paper.questions?.[0]).toMatchObject({ sourceQuestionId: 12, score: 5, order: 1 })
    expect(theoryPaperTotalScore(paper)).toBe(5)
    expect(validateTheoryPaper(paper)).toEqual([])
  })

  it('rejects empty and duplicate papers', () => {
    expect(validateTheoryPaper({ title: '', description: '', questions: [] })).toContain('请输入试卷名称。')
    const question = theoryPaperQuestionFromBank(bankQuestion)
    expect(validateTheoryPaper({ title: '重复', description: '', questions: [question, question] })).toContain('同一道题库题目不能重复加入试卷。')
  })

  it('uses an injectable random source without mutating candidates', () => {
    const source = [1, 2, 3, 4]
    expect(selectRandomTheoryQuestions(source, 2, () => 0)).toEqual([2, 3])
    expect(source).toEqual([1, 2, 3, 4])
  })

  it('reorders questions and rewrites their persisted order', () => {
    const first = theoryPaperQuestionFromBank(bankQuestion, 3, 1)
    const second = theoryPaperQuestionFromBank({ ...bankQuestion, id: 13, title: 'HTTP 状态码' }, 4, 2)
    const reordered = reorderTheoryPaperQuestions([first, second], 1, -1)
    expect(reordered.map((question) => question.sourceQuestionId)).toEqual([13, 12])
    expect(reordered.map((question) => question.order)).toEqual([1, 2])
    expect(normalizedTheoryPaper({ title: '排序', questions: reordered }).questions?.map((question) => question.sourceQuestionId)).toEqual([13, 12])
  })
})
