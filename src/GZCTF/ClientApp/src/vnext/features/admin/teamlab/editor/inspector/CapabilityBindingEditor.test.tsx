import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import { CapabilityBindingEditor } from './CapabilityBindingEditor'

const digest = 'sha256:' + 'a'.repeat(64)
vi.mock('swr', () => ({ default: (key: readonly string[]) => ({ data: { items: key[0].includes('device-packages') ? [{
  id: '01900000-0000-7000-8000-000000000007', bindingId: 7, name: 'modbus', displayName: 'MODBUS', version: '1.0',
  enabled: true, archived: false, supportedAssetKinds: ['docker'], digest: 'sha256:' + 'a'.repeat(64),
  cpuMillis: 1500, memoryMiB: 512, storageGib: 2,
}] : [] } }) }))
const node = { key: 'plc', type: 'docker', imageTemplateId: 1, devicePackageId: null,
  resources: { cpuUnits: 1, memoryMiB: 256, storageMiB: 1024 } } as TopologyAssetNode
describe('CapabilityBindingEditor', () => {
  it('binds the numeric topology identifier and chooses the matching image and resource minimums', () => {
    const change = vi.fn()
    render(<CapabilityBindingEditor node={node} onAssetChange={change} imageOptions={[{ id: 42, name: 'PLC', deviceType: 'docker', digest }]} />)
    fireEvent.change(screen.getByRole('combobox', { name: /设备包/ }), { target: { value: '7' } })
    expect(change).toHaveBeenCalledWith({ devicePackageId: 7, deviceParameters: null, imageTemplateId: 42,
      resources: { cpuUnits: 2, memoryMiB: 512, storageMiB: 2048 } })
  })
  it('explains unavailable artifacts instead of binding a package to an unrelated image', () => {
    render(<CapabilityBindingEditor node={node} onAssetChange={vi.fn()} imageOptions={[]} />)
    expect(screen.getByRole('option', { name: /请先导入对应镜像/ })).toBeDisabled()
  })
})
