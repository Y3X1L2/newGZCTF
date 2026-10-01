import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RuntimeTaskPanel } from './RuntimeTaskPanel'
import type { TeamLabRuntime } from '../api'

const runtime: TeamLabRuntime = { id: 'runtime', releaseId: 'release', generation: 1, status: 'running',
  stage: 'ready', openForAccess: true, shards: [], networks: [], assets: [], createdAt: 1, updatedAt: null, error: null }

describe('RuntimeTaskPanel', () => {
  it('shows actual blocking evidence and opens events', () => {
    const inspect = vi.fn()
    render(<RuntimeTaskPanel runtime={{ ...runtime, deploymentQueueTicketId: 'ticket-a', queueStatus: 'pending',
      currentOperationId: 'operation-a', subStages: [{ id: 'capacity-waiting', status: 'pending', message: '等待节点容量' }],
      failure: { code: 'runtime_capacity_exhausted', stage: 'capacity-waiting', retryable: false, detail: '当前节点容量不足。' },
    }} onInspect={inspect} />)
    expect(screen.queryByText('ticket-a')).toBeNull()
    expect(screen.queryByText('operation-a')).toBeNull()
    expect(screen.getByText('当前节点容量不足。')).toBeTruthy()
    expect(screen.queryByRole('button', { name: '重试' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '查看记录' }))
    expect(inspect).toHaveBeenCalledOnce()
  })
  it('does not fabricate a task when none exists', () => {
    const { container } = render(<RuntimeTaskPanel runtime={runtime} onInspect={vi.fn()} />)
    expect(container.textContent).toBe('')
  })
  it('renders execution state without claiming completion', () => {
    render(<RuntimeTaskPanel runtime={{ ...runtime, deploymentQueueTicketId: 'ticket-a', queueStatus: 'running' }} onInspect={vi.fn()} />)
    expect(screen.getByText('正在执行')).toBeTruthy()
    expect(screen.queryByText('已完成')).toBeNull()
  })
})
