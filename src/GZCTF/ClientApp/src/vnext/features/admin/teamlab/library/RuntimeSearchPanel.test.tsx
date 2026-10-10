import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RuntimeSearchPanel } from './RuntimeSearchPanel'
import { emptyRuntimeSearch } from './useRuntimeSearch'
import { teamLabRuntimeApi } from '../api'

const { search, submit } = vi.hoisted(() => ({ search: vi.fn(), submit: vi.fn() }))
vi.mock('./useRuntimeSearch', async importOriginal => ({ ...(await importOriginal<typeof import('./useRuntimeSearch')>()), useRuntimeSearch: search }))
describe('RuntimeSearchPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.clearAllMocks()
    search.mockReturnValue({ filters: emptyRuntimeSearch, setFilters: submit, mutate: vi.fn(), cursor: { page: 1, next: vi.fn(), previous: vi.fn() },
      data: { items: [{ id: 'runtime-a', topologyId: 'topology-a', releaseId: 'release-a', reference: 'Training Lab',
        generation: 2, status: 'ready', assetCount: 3, hasError: false, createdAt: 1788796800000 }], nextCursor: null } })
  })
  it('offers record deletion only for destroyed environments and requires confirmation', async () => {
    const result = search.getMockImplementation()!()
    search.mockReturnValue({ ...result, data: { items: [{ ...result.data.items[0], status: 'destroyed' }], nextCursor: null } })
    const remove = vi.spyOn(teamLabRuntimeApi, 'deleteRuntimeRecord').mockResolvedValue(undefined)
    render(<MemoryRouter><RuntimeSearchPanel /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: '删除运行记录 Training Lab' }))
    expect(remove).not.toHaveBeenCalled()
    const dialog = screen.getByRole('dialog', { name: '删除运行记录' })
    expect(dialog).toHaveTextContent('runtime-a')
    fireEvent.click(within(dialog).getByRole('button', { name: '删除运行记录' }))
    await vi.waitFor(() => expect(remove).toHaveBeenCalledWith('runtime-a'))
  })
  it('submits combined filters and links to the stable runtime detail path', () => {
    render(<MemoryRouter><RuntimeSearchPanel /></MemoryRouter>)
    expect(screen.queryByRole('button', { name: /删除运行记录/ })).toBeNull()
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
