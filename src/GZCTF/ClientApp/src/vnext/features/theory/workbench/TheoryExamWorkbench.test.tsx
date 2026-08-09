import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TheoryAnswerSheetStatus, TheoryQuestionType, TrainingCourseChapterTheoryPlayerPaperModel } from '@Api'
import { TheoryExamWorkbench } from './TheoryExamWorkbench'

function submittedPaper(
  overrides: Partial<TrainingCourseChapterTheoryPlayerPaperModel> = {}
): TrainingCourseChapterTheoryPlayerPaperModel {
  return {
    paperId: 6,
    title: 'Linux 课后测试',
    totalScore: 9,
    status: TheoryAnswerSheetStatus.Submitted,
    score: 3,
    submittedAt: Date.now(),
    updatedAt: Date.now(),
    showCorrectAnswerAfterSubmit: true,
    questions: [
      {
        id: 11,
        type: TheoryQuestionType.SingleChoice,
        title: '正确题',
        options: ['A', 'B'],
        score: 3,
        order: 1,
        answerIndexes: [1],
      },
      {
        id: 12,
        type: TheoryQuestionType.SingleChoice,
        title: '错误题',
        options: ['A', 'B'],
        score: 3,
        order: 2,
        answerIndexes: [1],
      },
      {
        id: 13,
        type: TheoryQuestionType.SingleChoice,
        title: '未答题',
        options: ['A', 'B'],
        score: 3,
        order: 3,
        answerIndexes: [0],
      },
    ],
    answers: [
      { paperQuestionId: 11, selectedIndexes: [1] },
      { paperQuestionId: 12, selectedIndexes: [0] },
      { paperQuestionId: 13, selectedIndexes: [] },
    ],
    ...overrides,
  }
}

const actions = {
  saveDraft: vi.fn(),
  submit: vi.fn(),
}

describe('TheoryExamWorkbench review mode', () => {
  it('shows the review summary and navigates directly through wrong questions', async () => {
    render(
      <TheoryExamWorkbench
        initialPaper={submittedPaper()}
        revealCorrectAnswers
        saveDraft={actions.saveDraft}
        submit={actions.submit}
      />
    )

    expect(screen.getByRole('heading', { name: '答卷复盘' })).toBeInTheDocument()
    expect(screen.getByText('正确题')).toBeInTheDocument()
    expect(screen.getByText('回答正确')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '仅看错题 (2)' }))

    await waitFor(() => expect(screen.getByText('错误题')).toBeInTheDocument())
    expect(screen.getByText('回答错误')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'AA' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'BB' })).not.toBeChecked()
    expect(screen.queryByText('我的答案')).not.toBeInTheDocument()
    expect(screen.queryByText('正确答案')).not.toBeInTheDocument()
  })

  it('does not fabricate a review for a historical sheet without answer rows', () => {
    render(
      <TheoryExamWorkbench
        initialPaper={submittedPaper({ answers: [] })}
        revealCorrectAnswers
        saveDraft={actions.saveDraft}
        submit={actions.submit}
      />
    )

    expect(screen.getByText(/这份历史答卷只保留了总成绩/)).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: '答卷复盘' })).not.toBeInTheDocument()
  })
})
