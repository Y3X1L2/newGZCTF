import { fireEvent, render, screen, within } from '@testing-library/react'
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
    render(<TaskHistoryPanel runtimeId="runtime-a" generation={3} currentStatus="running" />)
    expect(history).toHaveBeenCalledWith('runtime-a', 3)
    expect(screen.getByText('暂停')).toBeTruthy()
    expect(screen.getByText('第 2 代 · 正在停止')).toBeTruthy()
    expect(screen.getByText('执行节点不可用')).toBeTruthy()
    expect(screen.getByText('这次操作失败，当前环境仍在运行')).toBeTruthy()
    expect(screen.queryByText('node_unavailable')).toBeNull()
    expect(screen.queryByText('ticket-a')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /暂停/ }))
    expect(within(screen.getByRole('dialog')).getByText('node_unavailable')).toBeTruthy()
    expect(screen.getByRole('button', { name: '上一页' }).hasAttribute('disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: '下一页' }))
    expect(next).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('checkbox', { name: '当前运行' }))
    expect(filter).toHaveBeenCalledWith(true)
  })
  it('hides stale history and prevents advancing after a failed request', () => {
    const value = history.getMockImplementation()!()
    history.mockReturnValue({ ...value, page: 2, error: new Error('读取失败') })
    render(<TaskHistoryPanel runtimeId="runtime-a" generation={3} currentStatus="running" />)
    expect(screen.queryByText('ticket-a')).toBeNull()
    expect(screen.getByRole('button', { name: '下一页' }).hasAttribute('disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: '上一页' }))
    expect(previous).toHaveBeenCalledOnce()
  })
  it('classifies known failures and keeps raw codes in the detail drawer', () => {
    const value = history.getMockImplementation()!()
    const original = value.data.items[0]
    history.mockReturnValue({ ...value, data: { nextCursor: null, items: [
      { ...original, id: 'apply', stage: 'ready', errorCode: 'guest_network_apply_failed' },
      { ...original, id: 'interface', stage: 'failed', errorCode: 'guest_network_interface_missing' },
      { ...original, id: 'other', stage: 'future-stage', errorCode: 'operation.unclassified_failure' },
    ] } })
    render(<TaskHistoryPanel runtimeId="runtime-a" generation={3} currentStatus="running" />)
    expect(screen.getByText('第 2 代 · 已就绪')).toBeTruthy()
    expect(screen.getByText('第 2 代 · 执行失败')).toBeTruthy()
    expect(screen.getByText('第 2 代 · 其他执行阶段')).toBeTruthy()
    expect(screen.getByText('虚拟机网络配置失败')).toBeTruthy()
    expect(screen.getByText('虚拟机未识别需要配置的网卡')).toBeTruthy()
    expect(screen.getByText('操作失败，原因尚未分类')).toBeTruthy()
    expect(screen.queryByText('guest_network_apply_failed')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /虚拟机网络配置失败/ }))
    expect(within(screen.getByRole('dialog')).getByText('guest_network_apply_failed')).toBeTruthy()
    expect(within(screen.getByRole('dialog')).getByText('ready')).toBeTruthy()
  })
})
