import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { searchRuntimes } from '../api/teamlabRuntimeSearchApi'
import { useRuntimeSearch } from './useRuntimeSearch'

vi.mock('../api/teamlabRuntimeSearchApi', () => ({ searchRuntimes: vi.fn() }))

function SearchProbe() {
  const result = useRuntimeSearch()
  const location = useLocation()
  const navigate = useNavigate()
  return <>
    <span>page {result.cursor.page}</span>
    <span>cursor {result.cursor.cursor ?? 'first'}</span>
    <span>url {location.search}</span>
    <button onClick={() => result.cursor.next('next')} type="button">下一页</button>
    <button onClick={result.cursor.previous} type="button">上一页</button>
    <button onClick={() => result.setFilters({ ...result.filters, search: 'other' })} type="button">改筛选</button>
    <button onClick={() => navigate(-1)} type="button">浏览器返回</button>
  </>
}

describe('useRuntimeSearch URL state', () => {
  beforeEach(() => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined)
    vi.mocked(searchRuntimes).mockResolvedValue({ items: [], nextCursor: null })
  })

  it('restores the cursor and filters from history, and resets pagination for a new filter', async () => {
    render(<MemoryRouter initialEntries={['/admin/teamlab/runtimes?search=lab&after=old']}>
      <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><SearchProbe /></SWRConfig>
    </MemoryRouter>)
    expect(screen.getByText('page 2')).toBeInTheDocument()
    expect(screen.getByText('cursor old')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '下一页' }))
    expect(screen.getByText('page 3')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '改筛选' }))
    expect(screen.getByText('page 1')).toBeInTheDocument()
    expect(screen.getByText('url ?search=other')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '浏览器返回' }))
    await waitFor(() => expect(screen.getByText('page 3')).toBeInTheDocument())
    expect(screen.getByText('cursor next')).toBeInTheDocument()
    expect(searchRuntimes).toHaveBeenCalledWith(expect.objectContaining({ search: 'lab' }), 'next')
  })
})
