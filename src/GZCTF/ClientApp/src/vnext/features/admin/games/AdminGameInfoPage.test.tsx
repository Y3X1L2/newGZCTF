import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { GameInfoModel } from '@Api'
import { AdminGameInfoPage } from './AdminGameInfoPage'

const mocks = vi.hoisted(() => ({
  game: {
    id: 98,
    title: 'Phase 5B Theory',
    summary: '',
    content: '',
    gameType: 'Theory',
    hidden: true,
    practiceMode: false,
    isTest: true,
    acceptWithoutReview: true,
    inviteCode: null,
    teamMemberCountLimit: 0,
    containerCountLimit: 3,
    start: new Date('2026-07-16T18:00:00+08:00').getTime(),
    end: new Date('2026-07-16T20:00:00+08:00').getTime(),
    writeupRequired: false,
    writeupDeadline: new Date('2026-07-16T20:00:00+08:00').getTime(),
    writeupNote: '',
    bloodBonus: 0,
  } as GameInfoModel,
  mutateGame: vi.fn().mockResolvedValue(undefined),
  navigate: vi.fn(),
  update: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('react-router', async () => {
  const actual = await vi.importActual<typeof import('react-router')>('react-router')
  return {
    ...actual,
    useNavigate: () => mocks.navigate,
    useOutletContext: () => ({ game: mocks.game, mutateGame: mocks.mutateGame }),
  }
})

vi.mock('../api', () => ({
  gameAdminApi: {
    update: mocks.update,
  },
}))

describe('AdminGameInfoPage', () => {
  it('keeps an empty date-time input renderable and blocks invalid submission', () => {
    render(<AdminGameInfoPage />)

    const start = screen.getByLabelText(/开始时间/)
    const form = start.closest('form')
    expect(form).not.toBeNull()

    fireEvent.change(start, { target: { value: '' } })
    expect(start).toHaveValue('')

    fireEvent.submit(form as HTMLFormElement)
    expect(screen.getByText('请输入有效的比赛开始和结束时间。')).toBeVisible()
    expect(mocks.update).not.toHaveBeenCalled()
  })
})
