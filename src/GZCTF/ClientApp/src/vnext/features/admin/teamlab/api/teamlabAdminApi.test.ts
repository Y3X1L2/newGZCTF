import { describe, expect, it, vi } from 'vitest'
import type { RuntimeJsonClient } from '../../api/runtimeJsonClient'
import { createTeamLabAdminApi } from './teamlabAdminApi'
import { TeamLabContractError } from './teamlabErrors'
import { parseTeamLabTopologyDetail, serializeTeamLabWriteRequest } from './teamlabParsers'

function client(overrides: Partial<RuntimeJsonClient>): RuntimeJsonClient {
  const unexpected = async () => {
    throw new Error('Unexpected API call')
  }
  return {
    get: unexpected,
    postJson: unexpected,
    postForm: unexpected,
    putJson: unexpected,
    patchJson: unexpected,
    delete: unexpected,
    ...overrides,
  }
}

function topologyDetail() {
  return {
    id: '019f0000-0000-7000-8000-000000000001',
    revision: 3,
    schemaVersion: 2,
    definition: {
      name: 'Enterprise range',
      networks: [
        {
          key: 'edge',
          name: 'Edge',
          addressPool: { poolCidr: '10.20.0.0/16', runtimePrefixLength: 24 },
          isEntry: true,
          orderIndex: 0,
        },
      ],
      infrastructure: [{ key: 'switch-edge', name: 'Edge switch', kind: 0, interfaces: [], networkKey: 'edge' }],
      assets: [
        {
          key: 'portal',
          name: 'Portal',
          kind: 0,
          imageTemplateId: 12,
          resources: { cpuUnits: 1, memoryMiB: 512, storageMiB: 1024 },
          interfaces: [{ key: 'portal-edge-nic', networkKey: 'edge', hostOffset: 10, primary: true, orderIndex: 0 }],
          exposePort: 80,
          healthCheck: { kind: 1, port: 80 },
          orderIndex: 0,
        },
      ],
      connections: [],
      observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true },
    },
    editor: {
      networks: { edge: { x: 10, y: 20, width: null, height: null, collapsed: false } },
      assets: { portal: { x: 300, y: 20, width: 180, height: 100, collapsed: false } },
      infrastructure: { 'switch-edge': { x: 10, y: 20, width: null, height: null, collapsed: false } },
    },
    createdAt: 1_790_000_000_000,
    updatedAt: 1_790_000_001_000,
  }
}

describe('TeamLab admin contract boundary', () => {
  it.each([0, 1, 2])('round trips VM mode %s and explicit per-interface intent through the wire boundary', (mode) => {
    const source = topologyDetail()
    const wire = {
      ...source,
      definition: {
        ...source.definition,
        assets: source.definition.assets.map((asset) => ({
          ...asset,
          kind: 1,
          vmNetworkMode: mode,
          interfaces: asset.interfaces.map((iface) => ({
            ...iface,
            guestInterfaceName: 'eth0',
            useDefaultGateway: false,
            dnsServers: [],
            staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: '10.20.0.1', metric: null }],
          })),
        })),
      },
    }
    const parsed = parseTeamLabTopologyDetail(wire)
    const written = serializeTeamLabWriteRequest({ schemaVersion: 2, ...parsed.definition, editor: parsed.editor })
    expect(written.assets[0]).toMatchObject(wire.definition.assets[0])
    expect(parsed.definition.assets[0].interfaces[0].dnsServers).toEqual([])
    expect(parsed.definition.assets[0].interfaces[0].useDefaultGateway).toBe(false)
  })

  it('keeps inherited null distinct from omitted old fields and explicit empty lists', () => {
    const parsed = parseTeamLabTopologyDetail(topologyDetail())
    expect(parsed.definition.assets[0]).not.toHaveProperty('vmNetworkMode')
    expect(parsed.definition.assets[0].interfaces[0]).not.toHaveProperty('dnsServers')
    const assets = parsed.definition.assets.map((asset) => ({
      ...asset,
      vmNetworkMode: null,
      interfaces: asset.interfaces.map((iface) => ({
        ...iface,
        useDefaultGateway: null,
        dnsServers: null,
        staticRoutes: null,
      })),
    }))
    const written = serializeTeamLabWriteRequest({
      schemaVersion: 2,
      ...parsed.definition,
      assets,
      editor: parsed.editor,
    })
    const reloaded = parseTeamLabTopologyDetail({ ...topologyDetail(), definition: written })
    expect(reloaded.definition.assets[0]).toHaveProperty('vmNetworkMode', null)
    expect(reloaded.definition.assets[0].interfaces[0]).toMatchObject({
      useDefaultGateway: null,
      dnsServers: null,
      staticRoutes: null,
    })
  })

  it.each([
    { vmNetworkMode: 99 },
    { interfaces: [{ key: 'nic', networkKey: 'edge', hostOffset: 10, primary: true, dnsServers: '10.0.0.1' }] },
    { interfaces: [{ key: 'nic', networkKey: 'edge', hostOffset: 10, primary: true, useDefaultGateway: 'false' }] },
    {
      interfaces: [
        {
          key: 'nic',
          networkKey: 'edge',
          hostOffset: 10,
          primary: true,
          staticRoutes: [{ destinationCidr: '172.16.0.0/16', nextHop: 1 }],
        },
      ],
    },
  ])('rejects malformed network requirements at the adapter boundary', (patch) => {
    const source = topologyDetail()
    expect(() =>
      parseTeamLabTopologyDetail({
        ...source,
        definition: { ...source.definition, assets: [{ ...source.definition.assets[0], ...patch }] },
      })
    ).toThrow(TeamLabContractError)
  })

  it('parses wire enums and nullable fields into semantic transport types', () => {
    const parsed = parseTeamLabTopologyDetail(topologyDetail())

    expect(parsed.definition.infrastructure[0]?.kind).toBe('managed-switch')
    expect(parsed.definition.assets[0]).toMatchObject({ kind: 'docker' })
    expect(parsed.definition.assets[0]?.healthCheck?.kind).toBe('http')
  })

  it('rejects malformed unknown responses at the adapter boundary', () => {
    expect(() => parseTeamLabTopologyDetail({ ...topologyDetail(), revision: '3' })).toThrow(TeamLabContractError)
  })

  it('uses runtimeJsonClient and serializes semantic enums for create', async () => {
    const postJson = vi.fn().mockResolvedValue(topologyDetail())
    const api = createTeamLabAdminApi(client({ postJson }))
    const parsed = parseTeamLabTopologyDetail(topologyDetail())

    await api.createTopology({ schemaVersion: 2, ...parsed.definition, editor: parsed.editor })

    expect(postJson).toHaveBeenCalledWith(
      '/api/admin/teamlab/topologies',
      expect.objectContaining({
        schemaVersion: 2,
        infrastructure: [expect.objectContaining({ kind: 0 })],
        assets: [expect.objectContaining({ kind: 0, healthCheck: { kind: 1, port: 80 } })],
        observation: expect.objectContaining({ flowMetadataEnabled: true, onDemandPcapEnabled: true }),
      })
    )
  })

  it('parses the cursor scene projection and sends stable query parameters', async () => {
    const get = vi.fn().mockResolvedValue({
      items: [
        {
          id: topologyDetail().id,
          name: 'Range',
          ownerId: null,
          ownerDisplayName: 'admin',
          revision: 1,
          schemaVersion: 2,
          networkCount: 1,
          assetCount: 1,
          infrastructureCount: 1,
          latestRelease: null,
          validation: null,
          latestTrialRuntime: null,
          gameReferenceCount: 0,
          createdAt: topologyDetail().createdAt,
          updatedAt: topologyDetail().updatedAt,
        },
      ],
      nextCursor: null,
    })
    const api = createTeamLabAdminApi(client({ get }))
    await expect(api.listTopologies({ search: 'range', cursor: 'next', limit: 10 })).resolves.toMatchObject({
      items: [{ name: 'Range' }],
      nextCursor: null,
    })
    expect(get).toHaveBeenCalledWith('/api/admin/teamlab/topologies', {
      search: 'range',
      owner: undefined,
      ownerId: undefined,
      status: undefined,
      after: 'next',
      limit: 10,
    })
  })
})
