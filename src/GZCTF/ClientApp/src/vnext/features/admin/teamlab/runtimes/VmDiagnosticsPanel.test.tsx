import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { VmDiagnosticsPanel } from './VmDiagnosticsPanel'

const { read } = vi.hoisted(() => ({ read: vi.fn() }))
vi.mock('./useVmDiagnostics', () => ({ useVmDiagnostics: read }))
const runtime: TeamLabRuntime = { id: 'runtime-a', releaseId: 'release-a', generation: 3, status: 'running',
  stage: 'ready', openForAccess: true, shards: [], networks: [], createdAt: 0, updatedAt: null, error: null,
  assets: [{ id: 1, key: 'vm', name: 'VM', kind: 'vm', status: 'running', runtimeResourceId: 'vm-a', primaryIp: null, error: null }] }

describe('VmDiagnosticsPanel', () => {
  it('shows actual power state and refreshes without inventing guest health or logs', () => {
    const refresh = vi.fn()
    read.mockReturnValue({ data: { state: 'paused', nativeId: 'uuid-a', observedAt: 1788796800000 }, mutate: refresh })
    render(<VmDiagnosticsPanel runtime={runtime} />)
    expect(read).toHaveBeenLastCalledWith('runtime-a', 3, 1)
    expect(screen.getByText('已暂停')).toBeTruthy()
    expect(screen.getByText('uuid-a')).toBeTruthy()
    expect(screen.queryByText('健康')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '刷新 VM 状态' }))
    expect(refresh).toHaveBeenCalledOnce()
  })
  it('hides stale VM state after an identity failure', () => {
    read.mockReturnValue({ data: { state: 'running', nativeId: 'stale-uuid' }, error: new Error('VM 身份已变化'), mutate: vi.fn() })
    render(<VmDiagnosticsPanel runtime={runtime} />)
    expect(screen.getByText('VM 身份已变化')).toBeTruthy()
    expect(screen.queryByText('stale-uuid')).toBeNull()
  })
})
