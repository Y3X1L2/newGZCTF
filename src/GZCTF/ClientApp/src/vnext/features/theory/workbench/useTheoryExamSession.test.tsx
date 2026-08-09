import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TheoryAnswerSheetStatus, TheoryPlayerPaperModel } from '@Api'
import { useTheoryExamSession } from './useTheoryExamSession'

function paper(overrides: Partial<TheoryPlayerPaperModel> = {}): TheoryPlayerPaperModel {
  return {
    paperId: 6,
    status: TheoryAnswerSheetStatus.Draft,
    updatedAt: 100,
    questions: [],
    answers: [{ paperQuestionId: 11, selectedIndexes: [0] }],
    ...overrides,
  }
}

describe('useTheoryExamSession', () => {
  it('synchronizes a submitted answer sheet even when the paper id is unchanged', () => {
    const saveDraft = vi.fn()
    const submit = vi.fn()
    const { result, rerender, unmount } = renderHook(
      ({ initialPaper }) => useTheoryExamSession({ initialPaper, saveDraft, submit }),
      { initialProps: { initialPaper: paper() } }
    )

    expect(result.current.answers[11]).toEqual([0])

    act(() => {
      rerender({
        initialPaper: paper({
          status: TheoryAnswerSheetStatus.Submitted,
          updatedAt: 200,
          submittedAt: 200,
          answers: [{ paperQuestionId: 11, selectedIndexes: [1] }],
        }),
      })
    })

    expect(result.current.submitted).toBe(true)
    expect(result.current.answers[11]).toEqual([1])
    unmount()
  })
})
