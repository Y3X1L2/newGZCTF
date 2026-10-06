import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { teamLabServiceAccessApi } from '../api/teamlabServiceAccessApi'
import { ServiceAccessPanel } from './ServiceAccessPanel'

vi.mock('../api/teamlabServiceAccessApi', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/teamlabServiceAccessApi')>()
  return { ...actual, teamLabServiceAccessApi: { list: vi.fn(), create: vi.fn(), remove: vi.fn() } }
})

const runtime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 1, status: 'running', stage: 'ready', openForAccess: true,
  shards: [], networks: [{ key: 'office', name: '办公网', cidr: '10.96.0.0/24', gatewayIp: '10.96.0.1' }],
  assets: [{ id: 4, key: 'web', name: 'Web 服务', kind: 'docker', networkKeys: ['office'],
    runtimeResourceId: 'container-a', primaryIp: '10.96.0.10', status: 'running', error: null,
    interfaces: [{ key: 'eth0', networkKey: 'office', primary: true,
      assigned: { ipAddress: '10.96.0.10', prefixLength: 24, dnsServers: [], gatewayIp: null, staticRoutes: [] }, observed: null }] }],
  createdAt: 1, updatedAt: 1, error: null,
} as TeamLabRuntime

describe('ServiceAccessPanel', () => {
  beforeEach(() => {
    vi.mocked(teamLabServiceAccessApi.list).mockReset().mockResolvedValue([])
    vi.mocked(teamLabServiceAccessApi.create).mockReset().mockResolvedValue({
      id: 'access-a', runtimeId: runtime.id, generation: 1, assetId: 4, assetName: 'Web 服务', networkKey: 'office',
      protocol: 'tcp', internalPort: 80, publicPort: 32010, endpoint: 'gateway.example:32010', status: 'active',
      lastError: null, createdAt: 1, revokedAt: null,
    })
    vi.mocked(teamLabServiceAccessApi.remove).mockReset()
  })

  it('creates an automatically allocated public entry for the selected asset network', async () => {
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><ServiceAccessPanel runtime={runtime} assetId={4} /></SWRConfig>)
    await screen.findByText('暂无业务访问入口。')
    fireEvent.click(screen.getByRole('button', { name: '添加访问入口' }))
    fireEvent.change(screen.getByLabelText('内部端口'), { target: { value: '8080' } })
    fireEvent.click(screen.getByRole('button', { name: '开放访问' }))
    await waitFor(() => expect(teamLabServiceAccessApi.create).toHaveBeenCalledWith(runtime.id, 4, {
      protocol: 'tcp', internalPort: 8080, publicPort: null, networkKey: 'office',
    }))
  })
})
