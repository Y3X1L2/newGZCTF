import { describe, expect, it } from 'vitest'
import { challengeChapterIds } from './courseBindings'

describe('course challenge chapter bindings', () => {
  it('shows all real chapter bindings even when the course-level scalar is null', () => {
    expect(
      challengeChapterIds({ exerciseChallengeId: 20, chapterId: null }, [
        { id: 69, challenges: [{ exerciseChallengeId: 20 }] },
        { id: 70, challenges: [{ exerciseChallengeId: 20 }] },
        { id: 71, challenges: [{ exerciseChallengeId: 21 }] },
      ])
    ).toEqual([69, 70])
  })

  it('retains scalar compatibility and keeps unbound challenges unbound', () => {
    expect(challengeChapterIds({ exerciseChallengeId: 20, chapterId: 69 }, [])).toEqual([69])
    expect(
      challengeChapterIds({ exerciseChallengeId: 20 }, [{ id: 69, challenges: [{ exerciseChallengeId: 21 }] }])
    ).toEqual([])
  })
})
