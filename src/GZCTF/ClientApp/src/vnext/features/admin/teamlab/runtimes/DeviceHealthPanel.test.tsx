import { render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { DeviceHealthPanel } from './DeviceHealthPanel'
import { useDeviceHealth } from './useDeviceHealth'

vi.mock('./useDeviceHealth', () => ({ useDeviceHealth: vi.fn() }))
it('does not show an old healthy observation as a current healthy device', () => {
  vi.mocked(useDeviceHealth).mockReturnValue({ data: [{ assetId: 1, name: 'PLC', generation: 1, nextProbeAt: 1,
    observation: { status: 'healthy', observedAt: 1, errorCode: null, counters: [{ type: 'modbus.read', count: 12 }] } }], mutate: vi.fn() } as never)
  render(<DeviceHealthPanel runtimeId="runtime" generation={1} />)
  expect(screen.getByText('结果已过期，等待后台检查')).toBeInTheDocument()
  expect(screen.queryByText('正常')).not.toBeInTheDocument()
})
it('explains node failure while retaining explicitly historical counters', () => {
  vi.mocked(useDeviceHealth).mockReturnValue({ data: [{ assetId: 1, name: 'PLC', generation: 1, nextProbeAt: Date.now() + 60000,
    observation: { status: 'unavailable', observedAt: Date.now(), errorCode: 'device.node_unavailable', counters: [{ type: 'modbus.read', count: 12 }] } }], mutate: vi.fn() } as never)
  render(<DeviceHealthPanel runtimeId="runtime" generation={1} />)
  expect(screen.getByText('节点暂时不可查询，不能确认设备健康。')).toBeInTheDocument()
  expect(screen.getByText('上次采集：modbus.read：12')).toBeInTheDocument()
})
