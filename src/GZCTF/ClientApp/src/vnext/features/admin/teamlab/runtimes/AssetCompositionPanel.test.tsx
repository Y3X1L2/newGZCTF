import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime, TeamLabTopologyAsset } from '../api'
import { listTeamLabImageOptions, teamLabResourcesApi, teamLabRuntimeApi } from '../api'
import { AssetCompositionPanel } from './AssetCompositionPanel'

vi.mock('../api', () => ({
  listTeamLabImageOptions: vi.fn(),
  teamLabResourcesApi: { listDevicePackages: vi.fn() },
  teamLabRuntimeApi: { getAssetDefinition: vi.fn(), changeAssets: vi.fn() },
}))

const runtime: TeamLabRuntime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 1, planRevision: 2, status: 'running', stage: 'ready',
  openForAccess: true, shards: [], createdAt: 1, updatedAt: 1, error: null,
  networks: [{ key: 'lan', name: '办公网', cidr: '10.0.0.0/24', gatewayIp: '10.0.0.1' }],
  assets: [{ id: 1, key: 'vm', name: '域控', kind: 'vm', networkKeys: ['lan', 'backend'],
    runtimeResourceId: 'vm-a', primaryIp: '10.0.0.10', status: 'running', error: null }],
}
const definition: TeamLabTopologyAsset = {
  key: 'vm', name: '域控', kind: 'vm', imageTemplateId: 1,
  resources: { cpuUnits: 4, memoryMiB: 8192, storageMiB: 65536 },
  interfaces: [{ key: 'nic-a', networkKey: 'lan', hostOffset: 10, primary: true, orderIndex: 0 },
    { key: 'nic-b', networkKey: 'backend', hostOffset: 21, primary: false, orderIndex: 1 }],
  exposePort: 3389, healthCheck: { kind: 'tcp', port: 3389 }, orderIndex: 7,
  devicePackageId: null, deviceParameters: null, connectorId: null,
}

describe('AssetCompositionPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(listTeamLabImageOptions).mockResolvedValue([
      { id: 1, name: '当前 Windows', deviceType: 'windows-vm' },
      { id: 2, name: '新版 Windows', deviceType: 'windows-vm' },
    ])
    vi.mocked(teamLabResourcesApi.listDevicePackages).mockResolvedValue({ items: [], next: null })
    vi.mocked(teamLabRuntimeApi.getAssetDefinition).mockResolvedValue(definition)
  })

  it('replaces the template while preserving the actual interfaces and service settings', async () => {
    render(<AssetCompositionPanel runtime={runtime} initialAction="replace" initialAssetKey="vm" onSubmitted={vi.fn()} />)
    await waitFor(() => expect(screen.getByLabelText('镜像模板')).toHaveValue('1'))
    expect(screen.getByLabelText('内存 MiB')).toHaveValue(8192)
    expect(screen.queryByLabelText('接入网段')).toBeNull()
    fireEvent.change(screen.getByLabelText('镜像模板'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: '加入本轮变更' }))
    fireEvent.click(screen.getByRole('button', { name: '提交 1 项变更' }))
    await waitFor(() => expect(teamLabRuntimeApi.changeAssets).toHaveBeenCalledWith(runtime.id, expect.objectContaining({
      expectedPlanRevision: 2,
      replace: [expect.objectContaining({ imageTemplateId: 2, interfaces: definition.interfaces,
        exposePort: 3389, healthCheck: definition.healthCheck, orderIndex: 7 })],
    })))
  })

  it('keeps the replacement unavailable when its definition cannot be read', async () => {
    vi.mocked(teamLabRuntimeApi.getAssetDefinition).mockRejectedValue(new Error('运行资产缺少执行定义'))
    render(<AssetCompositionPanel runtime={runtime} initialAction="replace" initialAssetKey="vm" onSubmitted={vi.fn()} />)
    expect(await screen.findByText('运行资产缺少执行定义')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '加入本轮变更' })).toBeDisabled()
    expect(teamLabRuntimeApi.changeAssets).not.toHaveBeenCalled()
  })
})
