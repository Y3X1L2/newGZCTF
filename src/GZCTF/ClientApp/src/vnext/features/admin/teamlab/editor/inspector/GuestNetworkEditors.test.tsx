import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { TeamLabGuestNetworkRequirements } from '../../api/teamlabContracts'
import type { TopologyMembershipConnection } from '../../model/topologyDocument'
import { GuestInterfaceRequirementsEditor } from './GuestInterfaceRequirementsEditor'
import { GuestNetworkModeEditor } from './GuestNetworkModeEditor'

const initial: TopologyMembershipConnection = {
  type: 'membership',
  key: 'nic',
  interfaceKey: 'eth0',
  nodeKey: 'vm',
  switchKey: 'sw',
  hostOffset: 10,
  primary: true,
  orderIndex: 0,
}

function InterfaceHarness({
  onChange,
  source = initial,
  managedStatic = true,
  preconfigured = false,
  readOnly = false,
}: {
  onChange: (patch: TeamLabGuestNetworkRequirements) => void
  source?: TopologyMembershipConnection
  managedStatic?: boolean
  preconfigured?: boolean
  readOnly?: boolean
}) {
  const [connection, setConnection] = useState(source)
  return (
    <GuestInterfaceRequirementsEditor
      connection={connection}
      managedStatic={managedStatic}
      preconfigured={preconfigured}
      readOnly={readOnly}
      onChange={(patch) => {
        setConnection((previous) => ({ ...previous, ...patch }))
        onChange(patch)
      }}
    />
  )
}

describe('TeamLab guest network editors', () => {
  it('clears fixed-only settings only after an explicit action and keeps route intent', () => {
    const change = vi.fn()
    render(
      <InterfaceHarness
        managedStatic={false}
        onChange={change}
        source={{
          ...initial,
          guestInterfaceName: 'eth0',
          dnsServers: [],
          useDefaultGateway: false,
          staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: '10.0.0.1', metric: 10 }],
        }}
      />
    )
    expect(change).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '清除固定配置专用设置' }))
    expect(change).toHaveBeenLastCalledWith({
      guestInterfaceName: null,
      staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: '10.0.0.1', metric: null }],
    })
    expect(screen.queryByRole('button', { name: '清除固定配置专用设置' })).not.toBeInTheDocument()
  })

  it('reflects restored inherited DNS after an external document change', () => {
    const change = vi.fn()
    const view = render(
      <GuestInterfaceRequirementsEditor connection={initial} onChange={change} managedStatic preconfigured={false} />
    )
    fireEvent.change(screen.getByLabelText('DNS 配置'), { target: { value: 'custom' } })
    view.rerender(
      <GuestInterfaceRequirementsEditor
        connection={{ ...initial, dnsServers: [] }}
        onChange={change}
        managedStatic
        preconfigured={false}
      />
    )
    expect(screen.getByLabelText('DNS 配置')).toHaveValue('custom')
    view.rerender(
      <GuestInterfaceRequirementsEditor
        connection={{ ...initial, dnsServers: null }}
        onChange={change}
        managedStatic
        preconfigured={false}
      />
    )
    expect(screen.getByLabelText('DNS 配置')).toHaveValue('inherit')
    expect(screen.queryByRole('textbox', { name: /DNS 服务器/ })).not.toBeInTheDocument()
  })

  it('shows saved preconfigured mode without offering it for new assets or mutating on load', () => {
    const change = vi.fn()
    const view = render(<GuestNetworkModeEditor windows mode="preconfigured" onChange={change} />)
    expect(screen.getByRole('option', { name: '镜像预配置（待迁移）' })).toBeDisabled()
    expect(screen.getByLabelText('网络配置方式')).toHaveValue('preconfigured')
    expect(change).not.toHaveBeenCalled()
    view.rerender(<GuestNetworkModeEditor windows inheritedMode="dhcp" onChange={change} />)
    expect(screen.queryByRole('option', { name: '镜像预配置（待迁移）' })).not.toBeInTheDocument()
    expect(screen.getByText(/镜像默认：DHCP 自动获取/)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('网络配置方式'), { target: { value: 'managed-static' } })
    expect(change).toHaveBeenCalledWith('managed-static')
    view.rerender(<GuestNetworkModeEditor windows mode="managed-static" onChange={change} />)
    expect(screen.getByText(/QEMU Guest Agent/)).toBeInTheDocument()
    view.rerender(<GuestNetworkModeEditor windows={false} mode="managed-static" onChange={change} />)
    expect(screen.getByText(/QGA.*cloud-init \/ Netplan/)).toBeInTheDocument()
  })

  it('keeps false, no DNS and inherited DNS distinct, commits custom addresses by keyboard', async () => {
    const change = vi.fn()
    const user = userEvent.setup()
    render(<InterfaceHarness onChange={change} />)
    await user.click(screen.getByText('接口网络要求'))
    expect(change).not.toHaveBeenCalled()
    await user.selectOptions(screen.getByLabelText('默认网关'), 'none')
    expect(change).toHaveBeenLastCalledWith({ useDefaultGateway: false })
    await user.selectOptions(screen.getByLabelText('DNS 配置'), 'none')
    expect(change).toHaveBeenLastCalledWith({ dnsServers: [] })
    await user.selectOptions(screen.getByLabelText('DNS 配置'), 'inherit')
    expect(change).toHaveBeenLastCalledWith({ dnsServers: null })
    await user.selectOptions(screen.getByLabelText('DNS 配置'), 'custom')
    await user.type(screen.getByRole('textbox', { name: /DNS 服务器/ }), '127.0.0.1, 10.0.0.53{Enter}')
    expect(change).toHaveBeenLastCalledWith({ dnsServers: ['127.0.0.1', '10.0.0.53'] })
    await user.clear(screen.getByRole('textbox', { name: /DNS 服务器/ }))
    await user.tab()
    expect(change).toHaveBeenLastCalledWith({ dnsServers: [] })
  })

  it('limits routes, exposes metrics only for fixed configuration and removes routes explicitly', () => {
    const change = vi.fn()
    render(<InterfaceHarness onChange={change} />)
    fireEvent.click(screen.getByRole('button', { name: '添加路由' }))
    const target = screen.getByLabelText('路由 1 目标网段')
    fireEvent.change(target, { target: { value: '172.16.0.0/16' } })
    fireEvent.blur(target)
    const nextHop = screen.getByLabelText('路由 1 下一跳')
    fireEvent.change(nextHop, { target: { value: '10.0.0.1' } })
    fireEvent.blur(nextHop)
    expect(change).toHaveBeenLastCalledWith({
      staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: '10.0.0.1' }],
    })
    fireEvent.click(screen.getByRole('button', { name: '删除路由 1' }))
    expect(change).toHaveBeenLastCalledWith({ staticRoutes: [] })
    for (let index = 0; index < 8; index++) fireEvent.click(screen.getByRole('button', { name: '添加路由' }))
    expect(screen.getByRole('button', { name: '添加路由' })).toBeDisabled()
  })

  it('does not offer target names or metrics in DHCP and keeps preconfigured requirements read only', () => {
    const change = vi.fn()
    const view = render(
      <InterfaceHarness
        managedStatic={false}
        onChange={change}
        source={{ ...initial, staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: '10.0.0.1' }] }}
      />
    )
    expect(screen.queryByLabelText('来宾接口名称')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('路由 1 跃点数')).not.toBeInTheDocument()
    view.unmount()
    render(
      <InterfaceHarness
        preconfigured
        readOnly
        onChange={change}
        source={{ ...initial, dnsServers: [], useDefaultGateway: false }}
      />
    )
    expect(screen.getByLabelText('默认网关')).toBeDisabled()
    expect(screen.getByLabelText('DNS 配置')).toBeDisabled()
    expect(screen.getByRole('button', { name: '添加路由' })).toBeDisabled()
    expect(change).not.toHaveBeenCalled()
  })
})
