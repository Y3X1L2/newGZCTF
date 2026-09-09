import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { AssetFilesPanel } from './AssetFilesPanel'

const { execute } = vi.hoisted(() => ({ execute: vi.fn() }))
vi.mock('../api/teamlabAssetFilesApi', () => ({ executeAssetFile: execute }))
vi.mock('../../../../shared/Interaction', async importOriginal => ({
  ...await importOriginal<object>(),
  VNextConfirmDialog: ({ open, onConfirm, title }: { open: boolean; title: string; onConfirm: () => Promise<boolean> }) =>
    open ? <div role="dialog" aria-label={title}><button type="button" onClick={() => void onConfirm()}>确认操作</button></div> : null,
}))
const runtime: TeamLabRuntime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 3, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], createdAt: 0, updatedAt: null, error: null,
  assets: [1, 2].map(id => ({ id, key: `web-${id}`, name: `Web ${id}`, kind: 'docker', runtimeResourceId: `container-${id}`,
    primaryIp: null, status: 'running', error: null })),
}
describe('AssetFilesPanel', () => {
  beforeEach(() => { execute.mockReset() })
  it('reads actual directory entries and clears them when switching assets', async () => {
    execute.mockResolvedValue([{ name: '中文.txt', kind: 'file', size: 12 }])
    render(<AssetFilesPanel runtime={runtime} />)
    expect(execute).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '读取目录' }))
    await screen.findByText('中文.txt')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'list', '/')
    fireEvent.change(screen.getByLabelText('资产'), { target: { value: '2' } })
    expect(screen.queryByText('中文.txt')).toBeNull()
    expect(screen.getByText('尚未读取目录')).toBeTruthy()
  })
  it('requires confirmation before deletion and refreshes the real directory', async () => {
    execute.mockResolvedValueOnce([{ name: 'data.txt', kind: 'file', size: 1 }]).mockResolvedValue([])
    render(<AssetFilesPanel runtime={runtime} />)
    fireEvent.click(screen.getByRole('button', { name: '读取目录' }))
    await screen.findByText('data.txt')
    fireEvent.click(screen.getByRole('button', { name: '删除' }))
    expect(execute).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: '确认操作' }))
    await screen.findByText('此目录为空')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'delete', '/data.txt', undefined, true)
  })
  it('shows a request error without claiming an empty directory', async () => {
    execute.mockRejectedValue(new Error('路径不可读取'))
    render(<AssetFilesPanel runtime={runtime} />)
    fireEvent.click(screen.getByRole('button', { name: '读取目录' }))
    await screen.findByText('路径不可读取')
    expect(screen.queryByText('此目录为空')).toBeNull()
    await waitFor(() => expect(screen.getByRole('button', { name: '读取目录' })).not.toBeDisabled())
  })
  it('confirms SSH identity renewal without rebuilding the VM', async () => {
    execute.mockResolvedValue([])
    render(<AssetFilesPanel runtime={{ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm' }] }} />)
    fireEvent.click(screen.getByRole('button', { name: '重新登记 SSH 身份' }))
    expect(execute).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '确认操作' }))
    await screen.findByText('SSH 身份已重新登记，请重新读取目录。')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'reset-ssh-identity', '/', undefined, true)
  })
})
