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
    operatingSystem: 'windows', interfaces: [{ key: `nic-${id}`, networkKey: 'internal', primary: true,
      assigned: { ipAddress: `192.168.50.${id}`, prefixLength: 24, dnsServers: [], gatewayIp: null, staticRoutes: [] }, observed: null }],
    capabilities: [{ kind: 'console', status: 'configured-unverified', reason: '', settingsTemplateId: null },
      { kind: 'rdp', status: 'unconfigured', reason: '未配置 RDP', settingsTemplateId: null }],
  })),
}

function mount(value = runtime) {
  return render(<StrictMode><MemoryRouter initialEntries={['/?tab=assets&asset=1']}>
    <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
      <RuntimeAssetWorkspace runtime={value} />
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

  it('keeps one console and per-interface address after repeated asset switches', async () => {
    mount()
    await screen.findByText('未配置 RDP')
    expect(screen.queryByRole('region', { name: '更多机器工具' })).toBeNull()
    for (const id of [2, 3, 4, 1, 4, 2, 1]) {
      select(id)
      await waitFor(() => expect(teamLabRemoteAccessApi.getAvailability).toHaveBeenCalledWith('runtime', id))
      expect(screen.getAllByRole('region', { name: '远程连接' })).toHaveLength(1)
      expect(screen.getAllByRole('button', { name: '控制台' })).toHaveLength(1)
      expect(screen.getByRole('region', { name: '网卡与地址' })).toHaveTextContent(`192.168.50.${id}/24`)
      expect(screen.queryByRole('region', { name: '业务访问入口' })).toBeNull()
    }
  })

  it('closes an unfinished console for the previous asset instead of connecting it to the selection', async () => {
    let finish!: (session: TeamLabRemoteSession) => void
    const popup = { opener: null, closed: false, location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window)
    vi.spyOn(teamLabRemoteAccessApi, 'createConsoleSession').mockReturnValue(new Promise(resolve => { finish = resolve }))
    vi.spyOn(teamLabRemoteAccessApi, 'connect')
    mount()
    fireEvent.click(await screen.findByRole('button', { name: '控制台' }))
    await waitFor(() => expect(teamLabRemoteAccessApi.createConsoleSession).toHaveBeenCalledWith('runtime', 1))
    select(2)
    await act(async () => finish({
      id: 'old-console', runtimeId: 'runtime', assetId: 1, assetName: 'VM1', protocol: 'vnc', status: 'ready',
      reason: '', createdAt: 1, expiresAt: 1000, connectedAt: null, endedAt: null, endReason: null,
    }))
    expect(teamLabRemoteAccessApi.end).toHaveBeenCalledWith('old-console')
    expect(teamLabRemoteAccessApi.connect).not.toHaveBeenCalled()
    expect(popup.close).toHaveBeenCalled()
    expect(screen.getAllByRole('button', { name: '控制台' })).toHaveLength(1)
  })

  it('shows each allocated NIC separately without borrowing the network gateway or primary IP', () => {
    const dual = { ...runtime, networks: [...runtime.networks,
      { key: 'entry', name: 'Entry LAN', cidr: '10.1.0.0/24', gatewayIp: '10.1.0.254' }],
      assets: [{ ...runtime.assets[0], primaryIp: '192.168.50.1', interfaces: [
        { key: 'eth0', networkKey: 'internal', primary: true,
          assigned: { ipAddress: '192.168.50.1', prefixLength: 24, dnsServers: ['192.168.50.2'], gatewayIp: null, staticRoutes: [] }, observed: null },
        { key: 'eth1', networkKey: 'entry', primary: false,
          assigned: { ipAddress: '10.1.0.8', prefixLength: 24, dnsServers: [], gatewayIp: '10.1.0.1', staticRoutes: [] }, observed: null },
      ] }] } as TeamLabRuntime
    mount(dual)
    const interfaces = screen.getByRole('region', { name: '网卡与地址' })
    expect(interfaces).toHaveTextContent('192.168.50.1/24')
    expect(interfaces).toHaveTextContent('10.1.0.8/24')
    expect(interfaces).toHaveTextContent('来宾状态未核对')
    fireEvent.click(screen.getAllByText('DNS 与路由')[1])
    expect(interfaces).toHaveTextContent('10.1.0.1')
    expect(interfaces).not.toHaveTextContent('10.1.0.254')
    expect(screen.queryByRole('button', { name: '传文件' })).toBeNull()
  })

  it('keeps a guest readback distinct from the allocated address', () => {
    const source = runtime.assets[0]
    mount({ ...runtime, assets: [{ ...source, interfaces: [{ ...source.interfaces![0],
      observed: { ipAddress: '192.168.50.31', prefixLength: 24, dnsServers: ['192.168.50.3'],
        gatewayIp: null, staticRoutes: [], observedAt: 1788796800000 } }] }] })
    const interfaces = screen.getByRole('region', { name: '网卡与地址' })
    expect(interfaces).toHaveTextContent('192.168.50.1/24')
    expect(interfaces).toHaveTextContent('192.168.50.31/24')
    expect(interfaces).not.toHaveTextContent('来宾状态未核对')
    fireEvent.click(screen.getByText('DNS 与路由'))
    expect(interfaces).toHaveTextContent('回读 DNS')
    expect(interfaces).toHaveTextContent('192.168.50.3')
  })
})
