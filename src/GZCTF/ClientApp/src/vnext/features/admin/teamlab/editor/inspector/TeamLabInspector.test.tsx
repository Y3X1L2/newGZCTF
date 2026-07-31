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
      endpointObservation: 'optional',
    },
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
        routingEnabled: false,
        exposePort: 8080,
        environment: { MODE: 'production' },
        startCommand: '/app/start',
        healthCheck: { kind: 'http', port: 8080 },
        orderIndex: 2,
        stateless: true,
        bootstrap: { profileId: 'web', version: 3, parameters: { region: 'internal' } },
        endpointObservation: 'required',
        bakeAtPublish: false,
        imageDigest: 'sha256:abc',
      },
      database: {
        type: 'linux-vm',
        key: 'database',
        name: 'Database',
        position: { x: 520, y: 220, width: null, height: null, collapsed: false },
        imageTemplateId: 12,
        resources: { cpuUnits: 4, memoryMiB: 4096, storageMiB: 20_480 },
        routingEnabled: false,
        exposePort: null,
        environment: null,
        startCommand: null,
        healthCheck: { kind: 'tcp', port: 5432 },
        orderIndex: 3,
        stateless: false,
        bootstrap: null,
        endpointObservation: 'optional',
        bakeAtPublish: true,
        imageDigest: null,
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
      dependency: {
        type: 'dependency',
        key: 'dependency',
        assetKey: 'app',
        dependsOnKey: 'database',
        condition: 'service-ready',
      },
    },
  }
}

const selection = (nodeKeys: string[] = [], connectionKeys: string[] = []): TopologySelection => ({
  nodeKeys: new Set(nodeKeys),
  connectionKeys: new Set(connectionKeys),
})

describe('TeamLabInspector', () => {
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

    expect(screen.getByRole('option', { name: 'Web service (#42)' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Windows Server (#99)' })).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('镜像模板'), { target: { value: '42' } })

    const asset = (onDocumentChange.mock.calls[0][0] as TopologyDocument).nodes.app
    expect(asset).toMatchObject({
      imageTemplateId: 42,
      environment: { MODE: 'production' },
      startCommand: '/app/start',
      healthCheck: { kind: 'http', port: 8080 },
      bootstrap: { profileId: 'web', version: 3, parameters: { region: 'internal' } },
      endpointObservation: 'required',
      imageDigest: 'sha256:abc',
    })
    expect(screen.queryByRole('textbox', { name: /secret/i })).not.toBeInTheDocument()
  })

  it('updates membership, route and dependency connections with dedicated editors', () => {
    const membershipChange = vi.fn()
    const membershipView = render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={membershipChange}
        selection={selection([], ['app-edge'])}
      />
    )
    const hostOffset = screen.getByLabelText('主机偏移')
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

    const dependencyChange = vi.fn()
    render(
      <TeamLabInspector
        document={createDocument()}
        onDocumentChange={dependencyChange}
        selection={selection([], ['dependency'])}
      />
    )
    fireEvent.change(screen.getByLabelText('就绪条件'), { target: { value: 'bootstrap-completed' } })
    expect((dependencyChange.mock.calls[0][0] as TopologyDocument).connections.dependency).toMatchObject({
      condition: 'bootstrap-completed',
    })
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
