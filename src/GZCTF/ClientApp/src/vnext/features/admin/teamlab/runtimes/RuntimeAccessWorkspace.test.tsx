import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { describe, expect, it, vi } from 'vitest'
import { teamLabRuntimeApi, type TeamLabRuntime } from '../api'
import { teamLabServiceAccessApi } from '../api/teamlabServiceAccessApi'
import { RuntimeAccessWorkspace } from './RuntimeAccessWorkspace'

vi.mock('./RuntimeGrantPanel', () => ({ RuntimeGrantPanel: () => <div>平台权限设置</div> }))

const runtime: TeamLabRuntime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 1, status: 'running', stage: 'ready', openForAccess: false,
  shards: [], networks: [], assets: [], createdAt: 1, updatedAt: 1, error: null,
}

describe('RuntimeAccessWorkspace', () => {
  it('reads grants only when VPN is opened and never creates one on read', async () => {
    vi.spyOn(teamLabServiceAccessApi, 'list').mockResolvedValue([])
    vi.spyOn(teamLabRuntimeApi, 'listAccessGrants').mockResolvedValue([])
    const create = vi.spyOn(teamLabRuntimeApi, 'createAccessGrant')
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><RuntimeAccessWorkspace runtime={runtime} /></SWRConfig>)
    expect(teamLabRuntimeApi.listAccessGrants).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: /VPN 整网接入/ }))
    await waitFor(() => expect(teamLabRuntimeApi.listAccessGrants).toHaveBeenCalledWith(runtime.id))
    expect(create).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: '新增授权' })).toBeInTheDocument()
    expect(screen.getByText(/平台操作权限不会对外部端口/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '平台操作权限' }))
    expect(screen.getByText('平台权限设置')).toBeInTheDocument()
  })
})
