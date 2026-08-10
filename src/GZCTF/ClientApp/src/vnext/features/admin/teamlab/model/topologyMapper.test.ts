import { describe, expect, it } from 'vitest'
import type { TeamLabTopologyDetail } from '../api/teamlabContracts'
import { compileTopologyDocument } from './topologyCompiler'
import { mapTopologyDetailToDocument } from './topologyMapper'

const position = (x: number) => ({ x, y: 40, width: null, height: null, collapsed: false })

function detail(): TeamLabTopologyDetail {
  return {
    id: '019f0000-0000-7000-8000-000000000099',
    revision: 8,
    schemaVersion: 2,
    createdAt: 1_790_000_000_000,
    updatedAt: 1_790_000_001_000,
    definition: {
      name: 'Round trip',
      networks: [
        {
          key: 'client',
          name: 'Client',
          addressPool: { poolCidr: '10.50.0.0/16', runtimePrefixLength: 24 },
          isEntry: true,
          orderIndex: 0,
        },
        {
          key: 'domain',
          name: 'Domain',
          addressPool: { poolCidr: '172.22.0.0/16', runtimePrefixLength: 24 },
          isEntry: false,
          orderIndex: 1,
        },
      ],
      infrastructure: [
        { key: 'switch-client', name: 'Client switch', kind: 'managed-switch', interfaces: [], networkKey: 'client' },
        { key: 'switch-domain', name: 'Domain', kind: 'managed-switch', interfaces: [], networkKey: 'domain' },
        {
          key: 'router-main',
          name: 'Main router',
          kind: 'managed-router',
          networkKey: null,
          interfaces: [
            { key: 'router-client', networkKey: 'client', hostOffset: 1, primary: true, orderIndex: 0 },
            { key: 'router-domain', networkKey: 'domain', hostOffset: 1, primary: false, orderIndex: 1 },
          ],
        },
      ],
      assets: [
        {
          key: 'web',
          name: 'Web',
          kind: 'docker',
          imageTemplateId: 10,
          resources: { cpuUnits: 1, memoryMiB: 512, storageMiB: 2048 },
          interfaces: [{ key: 'web-client', networkKey: 'client', hostOffset: 10, primary: true, orderIndex: 0 }],
          exposePort: 8080,
          healthCheck: { kind: 'http', port: 8080 },
          orderIndex: 0,
          endpointObservation: 'required',
        },
        {
          key: 'dc',
          name: 'Domain controller',
          kind: 'vm',
          imageTemplateId: 20,
          resources: { cpuUnits: 4, memoryMiB: 8192, storageMiB: 40960 },
          interfaces: [{ key: 'dc-domain', networkKey: 'domain', hostOffset: 10, primary: true, orderIndex: 0 }],
          exposePort: null,
          healthCheck: { kind: 'tcp', port: 389 },
          orderIndex: 1,
          endpointObservation: 'optional',
        },
      ],
      connections: [
        {
          key: 'client-domain',
          fromNetworkKey: 'client',
          toNetworkKey: 'domain',
          viaAssetKey: null,
          viaNodeKey: 'router-main',
          direction: 'from-to',
        },
      ],
      dependencies: [{ assetKey: 'web', dependsOnKey: 'dc', condition: 'service-ready' }],
      observation: { flowMetadataEnabled: true, onDemandPcapEnabled: false, endpointObservation: 'required' },
    },
    editor: {
      networks: { client: position(0), domain: position(200) },
      infrastructure: { 'switch-client': position(0), 'switch-domain': position(200), 'router-main': position(100) },
      assets: { web: position(300), dc: position(400) },
    },
  }
}

describe('topology API round trip', () => {
  it('preserves all supported schema v2 semantics and VM device intent', () => {
    const source = detail()
    const document = mapTopologyDetailToDocument(source, {
      resolveVmDeviceType: (asset) => (asset.imageTemplateId === 20 ? 'windows-vm' : 'linux-vm'),
    })
    const compiled = compileTopologyDocument(document)

    expect(document.nodes.dc).toMatchObject({ type: 'windows-vm' })
    const { editor, schemaVersion, ...definition } = compiled
    const canonicalSource = {
      ...source.definition,
      assets: [...source.definition.assets].sort((left, right) => left.key.localeCompare(right.key)),
      infrastructure: [...source.definition.infrastructure].sort((left, right) => left.key.localeCompare(right.key)),
    }
    expect(schemaVersion).toBe(2)
    expect(definition).toEqual(canonicalSource)
    expect(editor).toEqual(source.editor)
  })

  it('requires image metadata to distinguish Linux and Windows VM nodes', () => {
    expect(() =>
      mapTopologyDetailToDocument(detail(), {
        resolveVmDeviceType: () => {
          throw new Error('missing image metadata')
        },
      })
    ).toThrow('missing image metadata')
  })

  it('reads old editor metadata and can classify the same VM as Linux from image facts', () => {
    const source = detail()
    source.editor = { ...source.editor, infrastructure: {} }
    const document = mapTopologyDetailToDocument(source, { resolveVmDeviceType: () => 'linux-vm' })

    expect(document.nodes.dc).toMatchObject({ type: 'linux-vm' })
    expect(document.nodes['switch-client']?.position).toEqual(source.editor.networks.client)
  })

  it('opens schema v1 topologies and upgrades them to schema v2 when compiled', () => {
    const source = detail()
    source.schemaVersion = 1
    source.definition.infrastructure = []
    source.definition.dependencies = []
    source.definition.observation = {
      flowMetadataEnabled: true,
      onDemandPcapEnabled: true,
      endpointObservation: 'optional',
    }
    source.definition.assets = source.definition.assets.map((asset) => ({
      ...asset,
      endpointObservation: 'disabled',
    }))
    source.definition.connections = []

    const document = mapTopologyDetailToDocument(source, { resolveVmDeviceType: () => 'linux-vm' })
    const compiled = compileTopologyDocument(document)

    expect(document.schemaVersion).toBe(2)
    expect(compiled.schemaVersion).toBe(2)
    expect(compiled.networks).toEqual(source.definition.networks)
    expect(compiled.assets).toEqual([...source.definition.assets].sort((left, right) => left.key.localeCompare(right.key)))
    expect(compiled.connections).toEqual(source.definition.connections)
  })

  it('still rejects unknown topology schema versions', () => {
    const source = detail()
    source.schemaVersion = 3

    expect(() => mapTopologyDetailToDocument(source, { resolveVmDeviceType: () => 'linux-vm' })).toThrow(
      'Topology schema version 3 is not supported by this editor.'
    )
  })

  it('preserves interface names that are reused by different assets', () => {
    const source = detail()
    source.definition.assets[0].interfaces[0].key = 'eth0'
    source.definition.assets[1].interfaces[0].key = 'eth0'

    const document = mapTopologyDetailToDocument(source, { resolveVmDeviceType: () => 'linux-vm' })
    const memberships = Object.values(document.connections).filter((connection) => connection.type === 'membership')
    const compiled = compileTopologyDocument(document)

    expect(memberships.filter((connection) => connection.nodeKey === 'web')).toHaveLength(1)
    expect(memberships.filter((connection) => connection.nodeKey === 'dc')).toHaveLength(1)
    expect(compiled.assets.find((asset) => asset.key === 'web')?.interfaces[0].key).toBe('eth0')
    expect(compiled.assets.find((asset) => asset.key === 'dc')?.interfaces[0].key).toBe('eth0')
  })
})
