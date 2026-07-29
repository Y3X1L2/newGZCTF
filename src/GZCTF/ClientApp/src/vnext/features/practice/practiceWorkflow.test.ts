import { describe, expect, it } from 'vitest'
import { calculatePracticeStats } from './PracticeStatsPage'
import { ExerciseDetailDto, ExerciseInfoDto } from './api/practiceApi'
import { resolvedPracticePhase } from './usePracticeInstance'

function exercise(overrides: Partial<ExerciseInfoDto>): ExerciseInfoDto {
  return {
    id: 1,
    title: 'exercise',
    difficulty: 'Easy',
    category: 'Web',
    tags: [],
    credit: false,
    type: 'StaticAttachment',
    isEnabled: true,
    acceptedCount: 0,
    submissionCount: 0,
    solved: false,
    userAcceptedCount: 0,
    userSubmissionCount: 0,
    ...overrides,
  }
}

function detail(overrides: Partial<ExerciseDetailDto>): ExerciseDetailDto {
  return {
    id: 1,
    title: 'exercise',
    category: 'Web',
    difficulty: 'Easy',
    tags: [],
    type: 'StaticContainer',
    credit: false,
    content: '',
    hints: [],
    flags: [],
    solvedFlagIds: [],
    attempts: 0,
    limit: null,
    solved: false,
    queue: null,
    context: {
      closeTime: null,
      instanceEntry: null,
      instanceEntryStatus: null,
      instanceEntryReadyAt: null,
      instanceEntryError: null,
      url: null,
      fileSize: null,
    },
    ...overrides,
  }
}

describe('practice workflow', () => {
  it('calculates statistics from the current user instead of global solves', () => {
    const stats = calculatePracticeStats([
      exercise({ solved: true, userAcceptedCount: 1, userSubmissionCount: 2 }),
      exercise({ id: 2, acceptedCount: 99, submissionCount: 150, userSubmissionCount: 1 }),
    ])

    expect(stats.solved).toBe(1)
    expect(stats.accuracy).toBe(33)
    expect(stats.byCategory.get('Web')).toEqual({ total: 2, solved: 1 })
  })

  it('maps queue operations to visible runtime phases', () => {
    const queue = {
      status: 'Pending',
      operation: 'Create',
      queuePosition: 3,
      peopleAhead: 2,
      targetNodeName: null,
      stageMessage: null,
      errorMessage: null,
    }
    expect(resolvedPracticePhase(detail({ queue }))).toBe('queued')
    expect(resolvedPracticePhase(detail({ queue: { ...queue, operation: 'Stop' } }))).toBe('stopping')
    expect(resolvedPracticePhase(detail({
      context: { ...detail({}).context, instanceEntry: '127.0.0.1:8080', closeTime: Date.now() + 60_000 },
    }))).toBe('running')
  })
})
