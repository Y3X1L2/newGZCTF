import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { RuntimeDifferencesPanel } from './RuntimeDifferencesPanel'

const { preview, repair } = vi.hoisted(() => ({ preview: vi.fn(), repair: vi.fn() }))
vi.mock('../api/teamlabDifferencesApi', () => ({ differencesApi: { preview, repair } }))
vi.mock('../../../../shared/Interaction', async importOriginal => ({
  ...await importOriginal<object>(),
  VNextConfirmDialog: ({ open, onConfirm, message }: { open: boolean; onConfirm: () => Promise<boolean>; message: string }) =>
    open ? <div role="dialog"><p>{message}</p><button onClick={() => void onConfirm()} type="button">提交修复</button></div> : null,
}))
const runtime = { id: 'runtime-a', generation: 3, assets: [] } as unknown as TeamLabRuntime
function Location() { return <output>{useLocation().search}</output> }
function mount() { render(<MemoryRouter><RuntimeDifferencesPanel runtime={runtime} /><Location /></MemoryRouter>) }
describe('RuntimeDifferencesPanel', () => {
  beforeEach(() => { preview.mockReset(); repair.mockReset().mockResolvedValue('ticket-a') })
  it('only reads after checking and confirms a repair before creating the existing asset task', async () => {
    preview.mockResolvedValue({ generation: 3, operationInProgress: false, items: [{ assetId: 1, name: 'Web', resourceKind: 'docker',
      workerNodeId: 'node-a', expectedState: 'running', actualState: 'exited', difference: 'power-drift', suggestedAction: 'start' }] })
    mount()
    expect(preview).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '检查差异' }))
    fireEvent.click(await screen.findByRole('button', { name: '启动' }))
    expect(repair).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '提交修复' }))
    await waitFor(() => expect(repair).toHaveBeenCalledWith('runtime-a', 1, 3, 'start', '修复运行状态差异'))
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('assetTask=ticket-a'))
  })
  it('does not offer destructive repair for unknown or conflicting node identities', async () => {
    preview.mockResolvedValue({ generation: 3, operationInProgress: false, items: [{ assetId: 1, name: 'PLC', resourceKind: 'vm',
      workerNodeId: 'node-a', expectedState: 'running', actualState: null, difference: 'identity-conflict', suggestedAction: null }] })
    mount()
    fireEvent.click(screen.getByRole('button', { name: '检查差异' }))
    await screen.findByText('资源身份冲突')
    expect(screen.queryByRole('button', { name: '重建' })).toBeNull()
    expect(repair).not.toHaveBeenCalled()
  })
})
