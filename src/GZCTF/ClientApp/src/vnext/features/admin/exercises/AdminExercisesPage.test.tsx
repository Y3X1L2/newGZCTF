import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminExercisesPage } from './AdminExercisesPage'

const mocks = vi.hoisted(() => ({ page: vi.fn(), mutate: vi.fn() }))
vi.mock('./exerciseAdminApi', () => ({ useAdminExercises: mocks.page, exerciseAdminApi: {} }))
vi.mock('./ExerciseEditorDrawer', () => ({ ExerciseEditorDrawer: () => null }))
vi.mock('../../../shared/useVNextPageTitle', () => ({ useVNextPageTitle: vi.fn() }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.page.mockImplementation((_query: string, page: number, pageSize: number) => ({ data: {
    items: [{ id: page, title: `page ${page}`, tags: [], isEnabled: false }],
    total: 75, page, pageSize, enabledCount: 60, containerCount: 35,
  }, mutate: mocks.mutate, isValidating: false }))
})

describe('admin exercise pagination', () => {
  it('uses server totals and moves search and page size to server queries', async () => {
    render(<MemoryRouter initialEntries={['/admin/exercises?page=2&q=old']}><AdminExercisesPage /></MemoryRouter>)
    expect(mocks.page).toHaveBeenLastCalledWith('old', 2, 20)
    expect(screen.getByText('75')).toBeVisible()
    expect(screen.getByText('60')).toBeVisible()
    expect(screen.getByText('35')).toBeVisible()
    fireEvent.change(screen.getByRole('searchbox', { name: '搜索练习题' }), { target: { value: 'new' } })
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith('new', 1, 20))
    fireEvent.click(screen.getByRole('button', { name: '下一页' }))
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith('new', 2, 20))
    fireEvent.change(screen.getByRole('combobox', { name: '每页题目数' }), { target: { value: '50' } })
    await waitFor(() => expect(mocks.page).toHaveBeenLastCalledWith('new', 1, 50))
  })

  it('normalizes invalid URL pagination before sending requests', () => {
    render(<MemoryRouter initialEntries={['/admin/exercises?page=-1&pageSize=999']}><AdminExercisesPage /></MemoryRouter>)
    expect(mocks.page).toHaveBeenLastCalledWith('', 1, 20)
  })
})
