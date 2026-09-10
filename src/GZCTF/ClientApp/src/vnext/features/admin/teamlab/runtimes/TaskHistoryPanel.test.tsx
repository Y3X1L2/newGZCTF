import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskHistoryPanel } from './TaskHistoryPanel'

const { history, next, previous, filter } = vi.hoisted(() => ({ history: vi.fn(), next: vi.fn(), previous: vi.fn(), filter: vi.fn() }))
vi.mock('./useTaskHistory', () => ({ useTaskHistory: history }))

describe('TaskHistoryPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    history.mockReturnValue({ currentOnly: false, page: 1, isLoading: false, isValidating: false,
      next, previous, setCurrentOnly: filter, mutate: vi.fn(), data: { nextCursor: 'cursor', items: [{
        id: 'ticket-a', generation: 2, operation: 'pause', status: 'failed', stage: 'stopping',
        createdAt: 1788796800000, completedAt: null, errorCode: 'node_unavailable', retryable: false,
      }] } })
  })
  it('shows task facts and supports pagination and generation filtering', () => {
    render(<TaskHistoryPanel runtimeId="runtime-a" generation={3} />)
    expect(history).toHaveBeenCalledWith('runtime-a', 3)
    expect(screen.getByText('暂停')).toBeTruthy()
    expect(screen.getByText('不可直接重试')).toBeTruthy()
    expect(screen.getByText('node_unavailable')).toBeTruthy()
    expect(screen.getByRole('button', { name: '上一页' }).hasAttribute('disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: '下一页' }))
    expect(next).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('checkbox', { name: '仅当前代次' }))
    expect(filter).toHaveBeenCalledWith(true)
  })
  it('hides stale history and prevents advancing after a failed request', () => {
    const value = history.getMockImplementation()!()
    history.mockReturnValue({ ...value, page: 2, error: new Error('读取失败') })
    render(<TaskHistoryPanel runtimeId="runtime-a" generation={3} />)
    expect(screen.queryByText('ticket-a')).toBeNull()
    expect(screen.getByRole('button', { name: '下一页' }).hasAttribute('disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: '上一页' }))
    expect(previous).toHaveBeenCalledOnce()
  })
})
