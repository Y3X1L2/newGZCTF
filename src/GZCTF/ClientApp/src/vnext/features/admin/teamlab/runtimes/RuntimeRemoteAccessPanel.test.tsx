import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SWRConfig } from 'swr'
import { teamLabRemoteAccessApi, type TeamLabRuntime, type TeamLabRemoteSession } from '../api'
import { RuntimeRemoteAccessPanel } from './RuntimeRemoteAccessPanel'

vi.mock('./ContainerTerminal', () => ({
  ContainerTerminal: ({ sessionId }: { sessionId: string | null }) => sessionId ? <div>terminal:{sessionId}</div> : null,
}))

const runtime: TeamLabRuntime = {
  id: 'runtime', releaseId: 'release', generation: 1, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], createdAt: 1, updatedAt: 1, error: null,
  assets: [{ id: 1, key: 'web', name: 'Web', kind: 'docker', networkKeys: [], runtimeResourceId: 'container', primaryIp: null, status: 'running', error: null }],
}
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
  return render(<StrictMode><SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
    <RuntimeRemoteAccessPanel runtime={value} assetId={1} />
  </SWRConfig></StrictMode>)
}

describe('RuntimeRemoteAccessPanel lifecycle', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(teamLabRemoteAccessApi, 'getAvailabilityBatch').mockResolvedValue([availability])
    vi.spyOn(teamLabRemoteAccessApi, 'getAvailability').mockResolvedValue(availability)
    vi.spyOn(teamLabRemoteAccessApi, 'list').mockResolvedValue({ items: [], nextCursor: null })
    vi.spyOn(teamLabRemoteAccessApi, 'createSession').mockResolvedValue(session)
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
    mount({ ...runtime, status: 'paused', assets: [{ ...runtime.assets[0], kind: 'vm', status: 'paused', primaryIp: null }] })
    fireEvent.click(await screen.findByRole('button', { name: 'VNC' }))
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
})
