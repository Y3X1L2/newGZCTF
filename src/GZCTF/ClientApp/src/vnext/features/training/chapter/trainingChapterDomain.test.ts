import { describe, expect, it } from 'vitest'
import { TheoryAnswerSheetStatus, TrainingCourseChapterModel, TrainingCourseProgressStatus } from '@Api'
import { chapterDepth, trainingChapterProgress } from './trainingChapterDomain'

describe('trainingChapterProgress', () => {
  it('requires every required lab when configured', () => {
    const progress = trainingChapterProgress({
      completionPolicy: { requireAllRequiredChallenges: true },
      challenges: [
        { exerciseChallengeId: 1, isRequired: true, solved: true },
        { exerciseChallengeId: 2, isRequired: true, solved: false },
        { exerciseChallengeId: 3, isRequired: false, solved: false },
      ],
    })

    expect(progress.requiredChallengeCount).toBe(2)
    expect(progress.solvedChallengeCount).toBe(1)
    expect(progress.challengesSatisfied).toBe(false)
    expect(progress.blockingConditions).toBe(false)
  })

  it('uses the configured theory pass rate without partial rounding', () => {
    const chapter: TrainingCourseChapterModel = {
      completionPolicy: { theoryPassRate: 80 },
      theoryPaper: {
        isPublished: true,
        totalScore: 10,
        score: 8,
        status: TheoryAnswerSheetStatus.Submitted,
      },
    }

    expect(trainingChapterProgress(chapter).theorySatisfied).toBe(true)
    expect(
      trainingChapterProgress({ ...chapter, theoryPaper: { ...chapter.theoryPaper, score: 7 } }).theorySatisfied
    ).toBe(false)
  })

  it('preserves a historically completed chapter while reporting current conditions', () => {
    const progress = trainingChapterProgress({
      progressStatus: TrainingCourseProgressStatus.Completed,
      completionPolicy: { requireContentRead: true },
      readPercent: 30,
    })

    expect(progress.completed).toBe(true)
    expect(progress.contentSatisfied).toBe(false)
  })
})

describe('chapterDepth', () => {
  it('caps visual nesting at two levels and follows the chapter tree', () => {
    const chapters: TrainingCourseChapterModel[] = [
      { id: 1 },
      { id: 2, parentId: 1 },
      { id: 3, parentId: 2 },
      { id: 4, parentId: 3 },
    ]

    expect(chapterDepth(chapters[0], chapters)).toBe(0)
    expect(chapterDepth(chapters[1], chapters)).toBe(1)
    expect(chapterDepth(chapters[3], chapters)).toBe(2)
  })
})
