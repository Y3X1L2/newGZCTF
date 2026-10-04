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
    networkKeys: [], primaryIp: null, status: 'running', error: null })),
}
describe('AssetFilesPanel', () => {
  beforeEach(() => { execute.mockReset() })
  it('reads actual directory entries and clears them when switching assets', async () => {
    execute.mockResolvedValue([{ name: '中文.txt', kind: 'file', size: 12 }])
    const view = render(<AssetFilesPanel runtime={runtime} assetId={1} />)
    await screen.findByText('中文.txt')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'list', '/')
    execute.mockResolvedValue([])
    view.rerender(<AssetFilesPanel runtime={runtime} assetId={2} />)
    await screen.findByText('空目录')
    expect(execute).toHaveBeenLastCalledWith('runtime-a', 2, 3, 'list', '/')
  })
  it('requires confirmation before deletion and refreshes the real directory', async () => {
    execute.mockResolvedValueOnce([{ name: 'data.txt', kind: 'file', size: 1 }]).mockResolvedValue([])
    render(<AssetFilesPanel runtime={runtime} assetId={1} />)
    await screen.findByText('data.txt')
    fireEvent.click(screen.getByRole('button', { name: 'data.txt' }))
    fireEvent.click(screen.getByRole('button', { name: '删除选中项' }))
    expect(execute).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: '确认操作' }))
    await screen.findByText('空目录')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'delete', '/data.txt', undefined, true, undefined, false)
  })
  it('shows a request error without claiming an empty directory', async () => {
    execute.mockRejectedValue(new Error('路径不可读取'))
    render(<AssetFilesPanel runtime={runtime} assetId={1} />)
    await screen.findByText('路径不可读取')
    expect(screen.queryByText('空目录')).toBeNull()
    await waitFor(() => expect(screen.getByRole('button', { name: '刷新目录' })).not.toBeDisabled())
  })
  it('confirms SSH identity renewal without rebuilding the VM', async () => {
    execute.mockResolvedValue([])
    render(<AssetFilesPanel runtime={{ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm' }] }} assetId={1} />)
    await screen.findByText('空目录')
    fireEvent.click(screen.getByRole('button', { name: '文件连接设置' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: '重新登记 SSH 身份' }))
    fireEvent.click(screen.getByRole('button', { name: '确认操作' }))
    await screen.findByText('SSH 身份已重新登记，请重新读取目录。')
    expect(execute).toHaveBeenCalledWith('runtime-a', 1, 3, 'reset-ssh-identity', '/', undefined, true)
  })
})
