import { describe, expect, it, vi } from 'vitest'
import type { RuntimeJsonClient } from '../../api/runtimeJsonClient'
import { createTeamLabAdminApi } from './teamlabAdminApi'
import { TeamLabContractError } from './teamlabErrors'
import { parseTeamLabTopologyDetail } from './teamlabParsers'

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
          endpointObservation: 1,
        },
      ],
      connections: [],
      dependencies: [],
      observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 1 },
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
  it('parses wire enums and nullable fields into semantic transport types', () => {
    const parsed = parseTeamLabTopologyDetail(topologyDetail())

    expect(parsed.definition.infrastructure[0]?.kind).toBe('managed-switch')
    expect(parsed.definition.assets[0]).toMatchObject({ kind: 'docker', endpointObservation: 'optional' })
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
        assets: [expect.objectContaining({ kind: 0, endpointObservation: 1, healthCheck: { kind: 1, port: 80 } })],
        observation: expect.objectContaining({ endpointObservation: 1 }),
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
