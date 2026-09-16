import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { expect, it, vi } from 'vitest'
import { CourseChallengesPanel } from './CourseChallengesPanel'

it('renders actual chapter bindings and only labels genuinely unbound challenges as unbound', () => {
  render(
    <MemoryRouter>
      <CourseChallengesPanel
        course={{
          id: 32,
          chapters: [
            { id: 69, challenges: [{ exerciseChallengeId: 627 }] },
            { id: 70, challenges: [{ exerciseChallengeId: 627 }] },
          ],
          challenges: [
            { exerciseChallengeId: 627, title: 'Bound lab', chapterId: null },
            { exerciseChallengeId: 628, title: 'Unbound lab', chapterId: null },
          ],
        }}
        onCourseChanged={vi.fn()}
      />
    </MemoryRouter>
  )
  expect(screen.getByText('章节 #69、#70')).toBeInTheDocument()
  expect(screen.getAllByText('未绑定章节')).toHaveLength(1)
})
