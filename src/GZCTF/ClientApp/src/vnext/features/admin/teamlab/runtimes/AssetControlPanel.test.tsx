import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { AssetControlPanel } from './AssetControlPanel'

const { submit, task, retry, availability } = vi.hoisted(() => ({ submit: vi.fn(), task: vi.fn(), retry: vi.fn(), availability: vi.fn() }))
vi.mock('../api/teamlabAssetControlApi', () => ({ assetControlApi: { submit, task, retry, availability } }))
vi.mock('../../../../shared/Interaction', async importOriginal => ({
  ...await importOriginal<object>(),
  VNextConfirmDialog: ({ open, onConfirm, onClose, message }: { open: boolean; message: string; onClose: () => void; onConfirm: () => Promise<boolean> }) =>
    open ? <div role="dialog"><p>{message}</p><button type="button" onClick={() => { void onConfirm().then(ok => { if (ok) onClose() }) }}>确认执行</button></div> : null,
}))
const runtime: TeamLabRuntime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 3, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], createdAt: 0, updatedAt: null, error: null,
  assets: [{ id: 1, key: 'web', name: 'Web', kind: 'docker', networkKeys: [], runtimeResourceId: 'container-1', primaryIp: null, status: 'running', error: null }],
}
function mount(value = runtime, path = '/?tab=operations') {
  return render(<MemoryRouter initialEntries={[path]}><SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
    <AssetControlPanel runtime={value} /></SWRConfig></MemoryRouter>)
}
describe('AssetControlPanel', () => {
  beforeEach(() => {
    submit.mockReset().mockResolvedValue('ticket-a')
    availability.mockReset().mockResolvedValue({ allowed: true, reason: null })
    retry.mockReset().mockResolvedValue('ticket-b')
    task.mockReset().mockResolvedValue({ id: 'ticket-a', status: 'pending', stage: '等待执行', errorCode: null, canRetry: false })
  })
  it('confirms a rebuild without requiring an operation reason', async () => {
    mount()
    await waitFor(() => expect(screen.getByRole('button', { name: '重启' })).toBeEnabled())
    fireEvent.click(screen.getByRole('button', { name: '更多资产操作' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: '重建' }))
    expect(submit).not.toHaveBeenCalled()
    expect(screen.getByText(/可写数据会丢失/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认执行' }))
    await screen.findByText('执行中')
    expect(submit).toHaveBeenCalledWith('runtime-a', 1, 3, 'rebuild')
    expect(screen.getByRole('button', { name: '重启' })).toBeDisabled()
  })
  it('shows stopped distinctly and offers start rather than resume', async () => {
    mount({ ...runtime, assets: [{ ...runtime.assets[0], status: 'stopped' }] })
    await waitFor(() => expect(screen.getByRole('button', { name: '启动' })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: '启动' }))
    await waitFor(() => expect(submit).toHaveBeenCalledWith('runtime-a', 1, 3, 'start'))
  })
  it('restores a failed task from the URL and continues its retained checkpoint', async () => {
    task.mockResolvedValueOnce({ id: 'ticket-a', status: 'failed', stage: '创建未完成', errorCode: 'asset_control.execution_failed', canRetry: true })
    mount(runtime, '/?tab=operations&asset=1&assetTask=ticket-a')
    await waitFor(() => expect(screen.getByRole('button', { name: '继续执行' })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: '继续执行' }))
    await waitFor(() => expect(retry).toHaveBeenCalledWith('runtime-a', 1, 'ticket-a'))
    expect(submit).not.toHaveBeenCalled()
  })
  it('does not turn remote-session permission into lifecycle control', async () => {
    availability.mockResolvedValue({ allowed: false, reason: '没有生命周期管理权限' })
    mount()
    await waitFor(() => expect(availability).toHaveBeenCalled())
    expect(screen.getByRole('button', { name: '重启' })).toBeDisabled()
    expect(submit).not.toHaveBeenCalled()
  })
})
