import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PlayerAccessPanel } from './PlayerAccessPanel'
import { teamLabPlayerApi } from './api'

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    teamLabPlayerApi: { ...actual.teamLabPlayerApi, createAccessGrant: vi.fn() },
  }
})

const runtimeId = '019f0000-0000-7000-8000-000000000001'
const grant = {
  id: '019f0000-0000-7000-8000-000000000002',
  type: 'WireGuard',
  clientAddress: '10.99.0.2/32',
  endpoint: 'vpn.example:51820',
  allowedIps: '10.20.0.0/24',
  dns: '10.20.0.1',
  createdAt: Date.now(),
  expiresAt: Date.now() + 60_000,
  configurationDownloadUrl: '/api/pentest/games/12/access-grants/grant/download?token=token',
}

describe('PlayerAccessPanel', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    vi.mocked(teamLabPlayerApi.createAccessGrant).mockReset().mockResolvedValue(grant)
  })

  it('confirms replacement semantics and restores the active grant after remount', async () => {
    const view = render(<PlayerAccessPanel gameId={12} ready runtimeId={runtimeId} />)
    fireEvent.click(screen.getByRole('button', { name: '获取 VPN 配置' }))

    expect(teamLabPlayerApi.createAccessGrant).not.toHaveBeenCalled()
    expect(screen.getByText(/会立即使队伍此前下载的 VPN 配置失效/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '确认签发' }))
    await waitFor(() => expect(screen.getByText('10.99.0.2/32')).toBeInTheDocument())

    view.unmount()
    render(<PlayerAccessPanel gameId={12} ready runtimeId={runtimeId} />)
    expect(screen.getByText('10.99.0.2/32')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '替换 VPN 配置' })).toBeInTheDocument()
  })
})
