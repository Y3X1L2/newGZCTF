import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { MemoryRouter } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabRemoteAccessApi, type TeamLabRuntime, type TeamLabRemoteSession } from '../api'
import { teamLabServiceAccessApi } from '../api/teamlabServiceAccessApi'
import { RuntimeAssetWorkspace } from './RuntimeAssetWorkspace'

vi.mock('./AssetControlPanel', () => ({ AssetControlPanel: () => null }))

const runtime: TeamLabRuntime = {
  id: 'runtime', releaseId: 'release', generation: 4, status: 'running', stage: 'ready',
  openForAccess: false, shards: [], createdAt: 1, updatedAt: 1, error: null,
  networks: [{ key: 'internal', name: 'Internal LAN', cidr: '192.168.50.0/24', gatewayIp: '192.168.50.254' }],
  assets: [1, 2, 3, 4].map(id => ({
    id, key: `vm${id}`, name: `VM${id}`, kind: 'vm', networkKeys: ['internal'],
    runtimeResourceId: `domain-${id}`, primaryIp: `192.168.50.${id}`, status: 'running', error: null,
  })),
}

function mount() {
  return render(<StrictMode><MemoryRouter initialEntries={['/?tab=assets&asset=1']}>
    <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
      <RuntimeAssetWorkspace runtime={runtime} onSubmitted={vi.fn()} />
    </SWRConfig>
  </MemoryRouter></StrictMode>)
}

function select(id: number) {
  fireEvent.click(screen.getByRole('button', { name: new RegExp(`^VM${id}\\s*192\\.168\\.50\\.${id}`) }))
}

describe('RuntimeAssetWorkspace asset switching', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(teamLabRemoteAccessApi, 'getAvailability').mockImplementation(async (_runtimeId, assetId) => ({
      assetId, assetName: `VM${assetId}`, protocol: 'rdp', available: false, unavailableReason: '未配置 RDP',
    }))
    vi.spyOn(teamLabRemoteAccessApi, 'list').mockResolvedValue({ items: [], nextCursor: null })
    vi.spyOn(teamLabServiceAccessApi, 'list').mockResolvedValue([])
    vi.spyOn(teamLabRemoteAccessApi, 'end').mockResolvedValue(undefined)
  })

  it('keeps one console and one service panel after repeated asset switches', async () => {
    mount()
    await screen.findByText('未配置 RDP')
    for (const id of [2, 3, 4, 1, 4, 2, 1]) {
      select(id)
      await waitFor(() => expect(teamLabRemoteAccessApi.getAvailability).toHaveBeenCalledWith('runtime', id))
      expect(screen.getAllByRole('region', { name: '远程连接' })).toHaveLength(1)
      expect(screen.getAllByRole('button', { name: 'VNC' })).toHaveLength(1)
      expect(screen.getAllByRole('region', { name: '开放服务' })).toHaveLength(1)
    }
  })

  it('closes an unfinished console for the previous asset instead of connecting it to the selection', async () => {
    let finish!: (session: TeamLabRemoteSession) => void
    const popup = { opener: null, closed: false, location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window)
    vi.spyOn(teamLabRemoteAccessApi, 'createConsoleSession').mockReturnValue(new Promise(resolve => { finish = resolve }))
    vi.spyOn(teamLabRemoteAccessApi, 'connect')
    mount()
    fireEvent.click(await screen.findByRole('button', { name: 'VNC' }))
    await waitFor(() => expect(teamLabRemoteAccessApi.createConsoleSession).toHaveBeenCalledWith('runtime', 1))
    select(2)
    await act(async () => finish({
      id: 'old-console', runtimeId: 'runtime', assetId: 1, assetName: 'VM1', protocol: 'vnc', status: 'ready',
      reason: '', createdAt: 1, expiresAt: 1000, connectedAt: null, endedAt: null, endReason: null,
    }))
    expect(teamLabRemoteAccessApi.end).toHaveBeenCalledWith('old-console')
    expect(teamLabRemoteAccessApi.connect).not.toHaveBeenCalled()
    expect(popup.close).toHaveBeenCalled()
    expect(screen.getAllByRole('button', { name: 'VNC' })).toHaveLength(1)
  })
})
