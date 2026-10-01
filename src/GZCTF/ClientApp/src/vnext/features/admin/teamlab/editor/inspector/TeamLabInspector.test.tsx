import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TopologyDocument } from '../../model/topologyDocument'
import type { TopologySelection } from '../../model/topologySelection'
import { TeamLabInspector } from './TeamLabInspector'

function createDocument(): TopologyDocument {
  return {
    schemaVersion: 2,
    name: 'Enterprise network',
    observation: {
      flowMetadataEnabled: true,
      onDemandPcapEnabled: true,
    },
    networkLayouts: {},
    nodes: {
      edge: {
        type: 'switch',
        key: 'edge',
        name: 'Edge switch',
        networkName: 'Entry network',
        networkKey: 'entry-net',
        poolCidr: '10.20.0.0/16',
        runtimePrefixLength: 24,
        isEntry: true,
        orderIndex: 0,
        position: { x: 10, y: 20, width: null, height: null, collapsed: false },
      },
      data: {
        type: 'switch',
        key: 'data',
        name: 'Data switch',
        networkName: 'Data network',
        networkKey: 'data-net',
        poolCidr: '172.16.0.0/16',
        runtimePrefixLength: 24,
        isEntry: false,
        orderIndex: 1,
        position: { x: 500, y: 20, width: 260, height: 140, collapsed: false },
      },
      router: {
        type: 'router',
        key: 'router',
        name: 'Core router',
        position: { x: 280, y: 20, width: null, height: null, collapsed: false },
      },
      app: {
        type: 'docker',
        key: 'app',
        name: 'Portal',
        position: { x: 80, y: 220, width: null, height: null, collapsed: false },
        imageTemplateId: 7,
        resources: { cpuUnits: 2, memoryMiB: 1024, storageMiB: 2048 },
        exposePort: 8080,
        healthCheck: { kind: 'http', port: 8080 },
        orderIndex: 2,
      },
      database: {
        type: 'linux-vm',
        key: 'database',
        name: 'Database',
        position: { x: 520, y: 220, width: null, height: null, collapsed: false },
        imageTemplateId: 12,
        resources: { cpuUnits: 4, memoryMiB: 4096, storageMiB: 20_480 },
        exposePort: null,
        healthCheck: { kind: 'tcp', port: 5432 },
        orderIndex: 3,
      },
    },
    connections: {
      'app-edge': {
        type: 'membership',
        key: 'app-edge',
        nodeKey: 'app',
        switchKey: 'edge',
        hostOffset: 10,
        primary: true,
        orderIndex: 0,
      },
      route: {
        type: 'route',
        key: 'route',
        fromSwitchKey: 'edge',
        toSwitchKey: 'data',
        viaNodeKey: 'router',
        direction: 'from-to',
      },
    },
  }
}

const selection = (nodeKeys: string[] = [], connectionKeys: string[] = []): TopologySelection => ({
  nodeKeys: new Set(nodeKeys),
  connectionKeys: new Set(connectionKeys),
})

describe('TeamLabInspector', () => {
  it.each(['asset', 'connection'])('keeps ordinary Docker %s editing free of VM network requirements', (target) => {
    const change = vi.fn()
    render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={change}
        selection={target === 'asset' ? selection(['app']) : selection([], ['app-edge'])}
      />
    )
    expect(screen.queryByText('接口网络要求')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('网络配置方式')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('DNS 配置')).not.toBeInTheDocument()
    expect(screen.getByLabelText('所属交换机')).toBeEnabled()
    expect(screen.getByRole('checkbox', { name: /主网卡/ })).toBeEnabled()
    const offset = screen.getByLabelText('主机偏移', { selector: 'input' })
    fireEvent.change(offset, { target: { value: '15' } })
    fireEvent.blur(offset)
    const iface = (change.mock.calls[0][0] as TopologyDocument).connections['app-edge']
    expect(iface).toMatchObject({ hostOffset: 15, primary: true })
    for (const field of ['guestInterfaceName', 'useDefaultGateway', 'dnsServers', 'staticRoutes']) {
      expect(iface).not.toHaveProperty(field)
    }
  })

  it('still exposes and saves per-interface VM network requirements', () => {
    const source = createDocument()
    const asset = source.nodes.app
    if (asset.type !== 'docker') throw new Error('invalid fixture')
    const vmDocument: TopologyDocument = {
      ...source,
      nodes: { ...source.nodes, app: { ...asset, type: 'linux-vm', vmNetworkMode: 'managed-static' } },
    }
    const change = vi.fn()
    render(<TeamLabInspector document={vmDocument} onDocumentChange={change} selection={selection(['app'])} />)
    expect(screen.getByText('接口网络要求')).toBeInTheDocument()
    expect(screen.getByLabelText('网络配置方式')).toHaveValue('managed-static')
    fireEvent.change(screen.getByLabelText('DNS 配置'), { target: { value: 'none' } })
    expect((change.mock.calls[0][0] as TopologyDocument).connections['app-edge']).toMatchObject({ dnsServers: [] })
    fireEvent.change(screen.getByLabelText('默认网关'), { target: { value: 'none' } })
    expect((change.mock.calls[1][0] as TopologyDocument).connections['app-edge']).toMatchObject({
      useDefaultGateway: false,
    })
  })

  it('edits a switch with the immutable command and preserves advanced fields', () => {
    const source = createDocument()
    const onDocumentChange = vi.fn()
    render(<TeamLabInspector document={source} onDocumentChange={onDocumentChange} selection={selection(['edge'])} />)

    const name = screen.getByLabelText('交换机名称')
    fireEvent.change(name, { target: { value: 'Ingress switch' } })
    expect(onDocumentChange).not.toHaveBeenCalled()
    fireEvent.blur(name)

    const updated = onDocumentChange.mock.calls[0][0] as TopologyDocument
    expect(updated.nodes.edge).toMatchObject({
      name: 'Ingress switch',
      networkKey: 'entry-net',
      runtimePrefixLength: 24,
    })
    expect(updated).not.toBe(source)
  })

  it('edits a network region without selecting it as a regular flow node', () => {
    const source = createDocument()
    const onDocumentChange = vi.fn()
    render(
      <TeamLabInspector
        document={source}
        onDocumentChange={onDocumentChange}
        selectedNetworkKey="entry-net"
        selection={selection()}
      />
    )

    expect(screen.getByText('网段区域')).toBeInTheDocument()
    const name = screen.getByRole('textbox', { name: /网段名称/ })
    fireEvent.change(name, { target: { value: '外部访问区' } })
    fireEvent.blur(name)
    expect((onDocumentChange.mock.calls[0][0] as TopologyDocument).nodes.edge).toMatchObject({
      networkName: '外部访问区',
      name: 'Edge switch',
    })

    fireEvent.click(screen.getByRole('checkbox', { name: /折叠区域/ }))
    expect((onDocumentChange.mock.calls[1][0] as TopologyDocument).networkLayouts['entry-net']).toMatchObject({
      collapsed: true,
    })
  })

  it('uses a compatible ready image option and preserves the complete asset contract', () => {
    const onDocumentChange = vi.fn()
    render(
      <TeamLabInspector
        document={createDocument()}
        imageOptions={[
          { id: 42, name: 'Web service', deviceType: 'docker' },
          { id: 99, name: 'Windows Server', deviceType: 'windows-vm' },
        ]}
        onDocumentChange={onDocumentChange}
        selection={selection(['app'])}
      />
    )

    expect(screen.getByRole('option', { name: 'Web service (#42) - 未配置运维接入' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Windows Server (#99)' })).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('镜像模板'), { target: { value: '42' } })

    const asset = (onDocumentChange.mock.calls[0][0] as TopologyDocument).nodes.app
    expect(asset).toMatchObject({
      imageTemplateId: 42,
      healthCheck: { kind: 'http', port: 8080 },
    })
    expect(screen.queryByRole('textbox', { name: /secret/i })).not.toBeInTheDocument()
  })

  it('updates membership and route connections', () => {
    const membershipChange = vi.fn()
    const membershipView = render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={membershipChange}
        selection={selection([], ['app-edge'])}
      />
    )
    const hostOffset = screen.getByLabelText('主机偏移', { selector: 'input' })
    fireEvent.change(hostOffset, { target: { value: '15' } })
    fireEvent.blur(hostOffset)
    expect((membershipChange.mock.calls[0][0] as TopologyDocument).connections['app-edge']).toMatchObject({
      hostOffset: 15,
    })
    membershipView.unmount()

    const routeChange = vi.fn()
    const routeView = render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={routeChange}
        selection={selection([], ['route'])}
      />
    )
    fireEvent.change(screen.getByLabelText('方向'), { target: { value: 'bidirectional' } })
    expect((routeChange.mock.calls[0][0] as TopologyDocument).connections.route).toMatchObject({
      direction: 'bidirectional',
    })
    routeView.unmount()
  })

  it('edits document observation with no selection and summarizes multiple selections', () => {
    const onDocumentChange = vi.fn()
    const observationView = render(
      <TeamLabInspector document={createDocument()} onDocumentChange={onDocumentChange} selection={selection()} />
    )
    expect(screen.getByLabelText('场景名称')).toHaveValue('Enterprise network')
    fireEvent.click(screen.getByLabelText('流量元数据'))
    expect((onDocumentChange.mock.calls[0][0] as TopologyDocument).observation.flowMetadataEnabled).toBe(false)
    observationView.unmount()

    render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={vi.fn()}
        selection={selection(['edge', 'app'], ['route'])}
      />
    )
    expect(screen.getByText('已选择多个对象。为避免批量覆盖异构配置，请单选后编辑属性。')).toBeInTheDocument()
    expect(screen.queryByLabelText('交换机名称')).not.toBeInTheDocument()
  })
})
