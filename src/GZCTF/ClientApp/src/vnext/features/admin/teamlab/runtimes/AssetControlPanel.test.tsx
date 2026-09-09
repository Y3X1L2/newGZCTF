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
  assets: [{ id: 1, key: 'web', name: 'Web', kind: 'docker', runtimeResourceId: 'container-1', primaryIp: null, status: 'running', error: null }],
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
  it('requires a reason and explicit confirmation before replacing one asset', async () => {
    mount()
    expect(screen.getByRole('button', { name: '重建' })).toBeDisabled()
    fireEvent.change(screen.getByLabelText('操作原因'), { target: { value: '重新验证原始环境' } })
    await waitFor(() => expect(screen.getByRole('button', { name: '重建' })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: '重建' }))
    expect(submit).not.toHaveBeenCalled()
    expect(screen.getByText(/可写层数据将丢失/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认执行' }))
    await screen.findByText('排队中')
    expect(submit).toHaveBeenCalledWith('runtime-a', 1, 3, 'rebuild', '重新验证原始环境')
    expect(screen.getByRole('button', { name: '重建' })).toBeDisabled()
  })
  it('shows stopped distinctly and offers start rather than resume', async () => {
    mount({ ...runtime, assets: [{ ...runtime.assets[0], status: 'stopped' }] })
    fireEvent.change(screen.getByLabelText('操作原因'), { target: { value: '恢复测试服务' } })
    expect(screen.getByText('已停止')).toBeTruthy()
    await waitFor(() => expect(screen.getByRole('button', { name: '启动' })).not.toBeDisabled())
    expect(screen.getByRole('button', { name: '恢复' })).toBeDisabled()
  })
  it('restores a failed task from the URL and continues its retained checkpoint', async () => {
    task.mockResolvedValueOnce({ id: 'ticket-a', status: 'failed', stage: '创建未完成', errorCode: 'asset_control.execution_failed', canRetry: true })
    mount(runtime, '/?tab=operations&asset=1&assetTask=ticket-a')
    await waitFor(() => expect(screen.getByRole('button', { name: '继续未完成步骤' })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: '继续未完成步骤' }))
    await waitFor(() => expect(retry).toHaveBeenCalledWith('runtime-a', 1, 'ticket-a'))
    expect(submit).not.toHaveBeenCalled()
  })
  it('does not turn remote-session permission into lifecycle control', async () => {
    availability.mockResolvedValue({ allowed: false, reason: '没有生命周期管理权限' })
    mount()
    fireEvent.change(screen.getByLabelText('操作原因'), { target: { value: '尝试操作资产' } })
    await screen.findByText('没有生命周期管理权限')
    expect(screen.getByRole('button', { name: '重建' })).toBeDisabled()
    expect(submit).not.toHaveBeenCalled()
  })
})
