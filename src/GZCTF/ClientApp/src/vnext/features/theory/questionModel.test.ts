import { describe, expect, it } from 'vitest'
import { TheoryQuestionType } from '@Api'
import {
  buildTheoryQuestionImportPlan,
  inspectTheoryQuestionJson,
  parseTheoryQuestionJson,
  theoryAnswerLabel,
} from './questionModel'

describe('theory question JSON', () => {
  it('parses all supported question types and answer formats', () => {
    const questions = parseTheoryQuestionJson(JSON.stringify({ questions: [
      { type: 'single', title: '单选', options: ['A1', 'B1'], answer: 'B' },
      { type: '多选', title: '多选', choices: ['A2', 'B2', 'C2'], answer: ['A', 'C'] },
      { type: '判断', title: '判断', answer: true },
    ] }), '验收题库')

    expect(questions.map((question) => question.type)).toEqual([
      TheoryQuestionType.SingleChoice,
      TheoryQuestionType.MultipleChoice,
      TheoryQuestionType.TrueFalse,
    ])
    expect(questions[0].answerIndexes).toEqual([1])
    expect(questions[1].answerIndexes).toEqual([0, 2])
    expect(theoryAnswerLabel(questions[2])).toBe('正确')
  })

  it('reports invalid questions without silently selecting the first option', () => {
    const result = inspectTheoryQuestionJson(JSON.stringify({ questions: [
      { type: 'single', title: '正常', options: ['A', 'B'], answer: 'A' },
      { type: 'single', title: '缺少答案', options: ['A', 'B'] },
      { type: 'unknown', title: '未知类型', options: ['A', 'B'], answer: 'A' },
    ] }))

    expect(result.questions).toHaveLength(1)
    expect(result.issues).toEqual([
      { index: 1, message: '缺少可识别的正确答案。' },
      { index: 2, message: '无法识别题型“unknown”。' },
    ])
  })
})

describe('theory import duplicate plan', () => {
  const question = {
    type: TheoryQuestionType.SingleChoice,
    bankName: 'Web',
    title: 'HTTP 状态码',
    content: '',
    options: ['200', '500'],
    answerIndexes: [0],
    tags: ['HTTP'],
  }

  it('supports skip, overwrite and copy strategies', () => {
    const existing = [{ ...question, id: 7 }]
    expect(buildTheoryQuestionImportPlan([question], existing, 'skip').skipCount).toBe(1)
    expect(buildTheoryQuestionImportPlan([question], existing, 'overwrite').items[0]).toMatchObject({ action: 'update', existingId: 7 })
    expect(buildTheoryQuestionImportPlan([question], existing, 'copy').createCount).toBe(1)
  })

  it('skips repeated questions inside one non-copy import', () => {
    const plan = buildTheoryQuestionImportPlan([question, question], [], 'overwrite')
    expect(plan.createCount).toBe(1)
    expect(plan.skipCount).toBe(1)
  })
})
