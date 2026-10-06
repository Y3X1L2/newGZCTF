import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SWRConfig } from 'swr'
import { teamLabRemoteAccessApi, type TeamLabRuntime, type TeamLabRemoteSession } from '../api'
import { RuntimeRemoteAccessPanel } from './RuntimeRemoteAccessPanel'

vi.mock('./ContainerTerminal', () => ({
  ContainerTerminal: ({ sessionId }: { sessionId: string | null }) => sessionId ? <div>terminal:{sessionId}</div> : null,
}))

const runtime = {
  id: 'runtime', releaseId: 'release', generation: 1, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], createdAt: 1, updatedAt: 1, error: null,
  assets: [{ id: 1, key: 'web', name: 'Web', kind: 'docker', networkKeys: [], runtimeResourceId: 'container', primaryIp: null, status: 'running', error: null,
    capabilities: [{ kind: 'terminal', status: 'configured-unverified', reason: '', settingsTemplateId: null }] }],
} as TeamLabRuntime
const availability = { assetId: 1, assetName: 'Web', protocol: 'containerTerminal' as const, available: true, unavailableReason: null }
const session: TeamLabRemoteSession = {
  id: 'session', runtimeId: 'runtime', assetId: 1, assetName: 'Web', protocol: 'containerTerminal', status: 'ready',
  reason: 'test reason', createdAt: 1, expiresAt: 1000, connectedAt: null, endedAt: null, endReason: null,
}

async function begin() {
  const button = await screen.findByRole('button', { name: '打开终端' })
  await waitFor(() => expect(button).toBeEnabled())
  fireEvent.click(button)
}

function mount(value = runtime) {
  return render(<StrictMode><MemoryRouter initialEntries={['/admin/teamlab/runtimes/runtime?asset=1']}><SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
    <RuntimeRemoteAccessPanel runtime={value} assetId={1} />
  </SWRConfig></MemoryRouter></StrictMode>)
}

describe('RuntimeRemoteAccessPanel lifecycle', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(teamLabRemoteAccessApi, 'getAvailabilityBatch').mockResolvedValue([availability])
    vi.spyOn(teamLabRemoteAccessApi, 'getAvailability').mockResolvedValue(availability)
    vi.spyOn(teamLabRemoteAccessApi, 'list').mockResolvedValue({ items: [], nextCursor: null })
    vi.spyOn(teamLabRemoteAccessApi, 'createSession').mockResolvedValue(session)
    vi.spyOn(teamLabRemoteAccessApi, 'createConsoleSession').mockResolvedValue({ ...session, protocol: 'vnc' })
    vi.spyOn(teamLabRemoteAccessApi, 'end').mockResolvedValue(undefined)
  })

  it('creates a terminal under StrictMode and cleans it on unmount', async () => {
    const view = mount()
    await begin()
    expect(await screen.findByText('terminal:session')).toBeInTheDocument()
    view.unmount()
    expect(teamLabRemoteAccessApi.end).toHaveBeenCalledWith('session')
  })

  it('opens a paused VM console without guest IP or SSH availability', async () => {
    const popup = { opener: null, closed: false, location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window)
    vi.spyOn(teamLabRemoteAccessApi, 'createConsoleSession').mockResolvedValue({ ...session, protocol: 'vnc' })
    vi.spyOn(teamLabRemoteAccessApi, 'connect').mockResolvedValue({ url: 'http://localhost/console', expiresAt: 1000 })
    vi.mocked(teamLabRemoteAccessApi.getAvailability).mockResolvedValue({ ...availability, available: false, unavailableReason: 'no guest access' })
    mount({ ...runtime, status: 'paused', assets: [{ ...runtime.assets[0], kind: 'vm', operatingSystem: 'unknown', status: 'paused', primaryIp: null,
      capabilities: [{ kind: 'console', status: 'configured-unverified', reason: '', settingsTemplateId: null }] }] } as TeamLabRuntime)
    fireEvent.click(await screen.findByRole('button', { name: '控制台' }))
    await waitFor(() => expect(popup.location.href).toBe('http://localhost/console'))
    expect(teamLabRemoteAccessApi.createConsoleSession).toHaveBeenCalledWith('runtime', 1)
    expect(teamLabRemoteAccessApi.createSession).not.toHaveBeenCalled()
  })

  it('compensates creation that finishes after unmount', async () => {
    let resolve!: (value: TeamLabRemoteSession) => void
    vi.mocked(teamLabRemoteAccessApi.createSession).mockReturnValue(new Promise((done) => { resolve = done }))
    const view = mount()
    await begin()
    await waitFor(() => expect(teamLabRemoteAccessApi.createSession).toHaveBeenCalled())
    view.unmount()
    await act(async () => resolve(session))
    expect(teamLabRemoteAccessApi.end).toHaveBeenCalledWith('session')
  })

  it('shows the actual connection failure', async () => {
    vi.mocked(teamLabRemoteAccessApi.createSession).mockRejectedValue(new Error('node unavailable'))
    mount()
    await begin()
    await screen.findByText('node unavailable')
    expect(screen.queryByText('terminal:session')).not.toBeInTheDocument()
  })

  it('never guesses SSH for an unknown VM and performs no write on read', async () => {
    mount({ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm', operatingSystem: 'unknown',
      capabilities: [{ kind: 'console', status: 'configured-unverified', reason: '', settingsTemplateId: null }] }] } as TeamLabRuntime)
    await waitFor(() => expect(teamLabRemoteAccessApi.getAvailability).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: 'SSH' })).toBeNull()
    expect(screen.queryByRole('button', { name: '远程桌面' })).toBeNull()
    expect(screen.getByRole('button', { name: '控制台' })).toBeInTheDocument()
    expect(teamLabRemoteAccessApi.createSession).not.toHaveBeenCalled()
    expect(teamLabRemoteAccessApi.createConsoleSession).not.toHaveBeenCalled()
  })

  it('shows Windows configuration scope and unavailable console reason', async () => {
    mount({ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm', operatingSystem: 'windows', sourceTemplateId: 42,
      capabilities: [{ kind: 'console', status: 'currently-unavailable', reason: '宿主节点离线', settingsTemplateId: null },
        { kind: 'rdp', status: 'unconfigured', reason: '尚未配置 RDP', settingsTemplateId: 42 }] }] } as TeamLabRuntime)
    expect(screen.queryByRole('button', { name: 'SSH' })).toBeNull()
    expect(screen.queryByRole('button', { name: '控制台' })).toBeNull()
    expect(screen.getByRole('link', { name: '配置远程桌面入口' })).toHaveAttribute('href', expect.stringContaining('/admin/images?template=42'))
    expect(screen.getByText(/宿主节点离线/)).toBeInTheDocument()
  })

  it('offers Linux SSH only when its declared capability matches availability', async () => {
    vi.mocked(teamLabRemoteAccessApi.getAvailability).mockResolvedValue({ ...availability, protocol: 'ssh' })
    mount({ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm', operatingSystem: 'linux',
      capabilities: [{ kind: 'console', status: 'configured-unverified', reason: '', settingsTemplateId: null },
        { kind: 'ssh', status: 'configured-unverified', reason: '', settingsTemplateId: 52 }] }] } as TeamLabRuntime)
    expect(await screen.findByRole('button', { name: 'SSH' })).toBeEnabled()
    expect(screen.getByRole('button', { name: '控制台' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '远程桌面' })).toBeNull()
  })
})
