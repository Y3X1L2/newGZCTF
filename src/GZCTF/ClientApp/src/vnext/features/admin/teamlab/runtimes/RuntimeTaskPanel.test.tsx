import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RuntimeTaskPanel } from './RuntimeTaskPanel'

describe('RuntimeTaskPanel', () => {
  it('shows actual blocking evidence and opens events', () => {
    const inspect = vi.fn()
    render(<RuntimeTaskPanel runtime={{ deploymentQueueTicketId: 'ticket-a', queueStatus: 'pending',
      currentOperationId: 'operation-a', subStages: [{ id: 'capacity-waiting', status: 'pending', message: '等待节点容量' }],
      failure: { code: 'runtime_capacity_exhausted', stage: 'capacity-waiting', retryable: false, detail: '当前节点容量不足。' },
    }} onInspect={inspect} />)
    expect(screen.getByText('ticket-a')).toBeTruthy()
    expect(screen.getByText('operation-a')).toBeTruthy()
    expect(screen.getByText('等待节点容量')).toBeTruthy()
    expect(screen.getByText('不能直接重试，请先检查运行事件。')).toBeTruthy()
    expect(screen.queryByRole('button', { name: '重试' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '查看运行事件' }))
    expect(inspect).toHaveBeenCalledOnce()
  })
  it('does not fabricate a task when none exists', () => {
    const { container } = render(<RuntimeTaskPanel runtime={{}} onInspect={vi.fn()} />)
    expect(container.textContent).toBe('')
  })
  it('renders execution state without claiming completion', () => {
    render(<RuntimeTaskPanel runtime={{ deploymentQueueTicketId: 'ticket-a', queueStatus: 'running' }} onInspect={vi.fn()} />)
    expect(screen.getByText('执行中')).toBeTruthy()
    expect(screen.queryByText('已完成')).toBeNull()
  })
})
