import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { teamLabResourcesApi } from '../api'
import { ConnectorRegisterDialog } from './capabilityRegisterDialogs'

vi.mock('./useTeamLabResources', () => ({
  useConnectorNodes: () => ({ data: [{ id: '01900000-0000-7000-8000-000000000083', name: '实验节点' }] }),
}))

it('registers a dedicated NIC with the selected node and does not offer unsupported providers', async () => {
  const register = vi.spyOn(teamLabResourcesApi, 'registerConnector').mockResolvedValue({} as never)
  const completed = vi.fn()
  render(<ConnectorRegisterDialog open onClose={vi.fn()} onRegistered={completed} />)
  fireEvent.change(screen.getByLabelText('名称（唯一标识）'), { target: { value: 'lab-nic' } })
  fireEvent.change(screen.getByLabelText('显示名称'), { target: { value: '实验专用网卡' } })
  fireEvent.change(screen.getByLabelText('所属节点'), { target: { value: '01900000-0000-7000-8000-000000000083' } })
  fireEvent.change(screen.getByLabelText('专用网卡名称'), { target: { value: 'enp2s0' } })
  fireEvent.change(screen.getByLabelText('网卡 MAC 地址'), { target: { value: '02:00:00:00:00:83' } })
  expect(screen.getByRole('option', { name: '串口（尚未支持执行）' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: '登记连接器' }))
  await waitFor(() => expect(completed).toHaveBeenCalledOnce())
  expect(register).toHaveBeenCalledWith(expect.objectContaining({
    kind: 'managed-nic', supportsSharedUse: false, capacity: 1,
    managedNic: { nodeId: '01900000-0000-7000-8000-000000000083', interfaceName: 'enp2s0', macAddress: '02:00:00:00:00:83' },
  }))
  register.mockRestore()
})
