import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RuntimeSearchPanel } from './RuntimeSearchPanel'
import { emptyRuntimeSearch } from './useRuntimeSearch'

const { search, submit } = vi.hoisted(() => ({ search: vi.fn(), submit: vi.fn() }))
vi.mock('./useRuntimeSearch', async importOriginal => ({ ...(await importOriginal<typeof import('./useRuntimeSearch')>()), useRuntimeSearch: search }))
describe('RuntimeSearchPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    search.mockReturnValue({ filters: emptyRuntimeSearch, setFilters: submit, mutate: vi.fn(), cursor: { page: 1, next: vi.fn(), previous: vi.fn() },
      data: { items: [{ id: 'runtime-a', topologyId: 'topology-a', releaseId: 'release-a', reference: 'Training Lab',
        generation: 2, status: 'ready', assetCount: 3, hasError: false, createdAt: 1788796800000 }], nextCursor: null } })
  })
  it('submits combined filters and links to the stable runtime detail path', () => {
    render(<MemoryRouter><RuntimeSearchPanel /></MemoryRouter>)
    expect(screen.queryByLabelText('运行代次')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '高级筛选' }))
    fireEvent.change(screen.getByLabelText('节点名称'), { target: { value: 'worker-a' } })
    fireEvent.change(screen.getByLabelText('运行代次'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: '查询实例' }))
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ node: 'worker-a', generation: '2' }))
    expect(screen.getByRole('link', { name: '进入环境 Training Lab' }).getAttribute('href')).toBe('/admin/teamlab/runtimes/runtime-a?from=runtime-search')
  })
  it('opens a runtime even when its topology link is absent', () => {
    const result = search.getMockImplementation()!()
    search.mockReturnValue({ ...result, data: { items: [{ ...result.data.items[0], topologyId: null }], nextCursor: null } })
    render(<MemoryRouter><RuntimeSearchPanel /></MemoryRouter>)
    expect(screen.getByRole('link', { name: '进入环境 Training Lab' })).toHaveAttribute('href', '/admin/teamlab/runtimes/runtime-a?from=runtime-search')
  })
  it('does not expose stale results after a failed search', () => {
    const result = search.getMockImplementation()!()
    search.mockReturnValue({ ...result, error: new Error('没有查询权限') })
    render(<MemoryRouter><RuntimeSearchPanel /></MemoryRouter>)
    expect(screen.getByText('没有查询权限')).toBeTruthy()
    expect(screen.queryByText('Training Lab')).toBeNull()
  })
})
