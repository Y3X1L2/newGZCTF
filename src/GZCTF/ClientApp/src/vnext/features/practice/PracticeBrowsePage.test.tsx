import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PracticeBrowsePage } from './PracticeBrowsePage'

const mocks = vi.hoisted(() => ({ page: vi.fn() }))
vi.mock('./api/practiceApi', () => ({ useExercisePage: mocks.page }))
vi.mock('../account/useCurrentAccount', () => ({ useCurrentAccount: () => ({ isTeacher: true }) }))
vi.mock('../../shared/useVNextPageTitle', () => ({ useVNextPageTitle: vi.fn() }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.page.mockImplementation((_filter: string, page: number, pageSize: number) => ({ data: {
    items: [{ id: page, title: `page ${page}`, category: 'Web', difficulty: 'Baby', tags: [], solved: true }],
    total: 75, page, pageSize, tags: ['tag on another page'],
  }, isValidating: false }))
})

describe('practice catalog pagination', () => {
  it('sends all filters before paging, uses total count and requests only the next page', async () => {
    render(<MemoryRouter initialEntries={['/practice?page=2&q=search&tag=test&status=solved&category=Web&source=Game']}><PracticeBrowsePage /></MemoryRouter>)
    expect(mocks.page).toHaveBeenLastCalledWith('Search=search&Categories=Web&Tags=test&Solved=true&Sources=Game', 2, 20)
    expect(screen.getByText('共 75 道题目')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: '下一页' }))
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith(expect.any(String), 3, 20))
    expect(screen.queryByText('page 2')).not.toBeInTheDocument()
    expect(screen.getByText('page 3')).toBeVisible()
  })

  it('keeps tags from the whole visible catalog and resets the page for filters and page size', async () => {
    render(<MemoryRouter initialEntries={['/practice?page=3']}><PracticeBrowsePage /></MemoryRouter>)
    fireEvent.click(screen.getByText('tag on another page'))
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith('Tags=tag+on+another+page', 1, 20))
    fireEvent.change(screen.getByRole('combobox', { name: '每页题目数' }), { target: { value: '50' } })
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith('Tags=tag+on+another+page', 1, 50))
  })
})
