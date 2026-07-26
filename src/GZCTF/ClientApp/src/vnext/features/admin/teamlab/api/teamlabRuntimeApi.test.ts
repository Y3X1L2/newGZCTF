import { describe, expect, it, vi } from 'vitest'
import type { RuntimeJsonClient } from '../../api/runtimeJsonClient'
import { createTeamLabRuntimeApi } from './teamlabRuntimeApi'
import { TeamLabContractError } from './teamlabErrors'
import {
  parseTeamLabCapture,
  parseTeamLabRuntime,
  parseTeamLabTrafficPath,
} from './teamlabRuntimeParsers'

function runtimeClient(overrides: Partial<RuntimeJsonClient> = {}): RuntimeJsonClient {
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

const runtimeWire = {
  id: '019f0000-0000-7000-8000-000000000101',
  releaseId: '019f0000-0000-7000-8000-000000000102',
  generation: 2,
  status: 5,
  stage: 'ready',
  openForAccess: true,
  shards: [
    {
      id: '019f0000-0000-7000-8000-000000000103',
      workerNodeId: '019f0000-0000-7000-8000-000000000104',
      workerNodeName: 'worker-a',
      status: 5,
      networkKeys: ['edge'],
      assetKeys: ['portal'],
      error: null,
    },
  ],
  networks: [{ key: 'edge', name: 'Edge', cidr: '10.20.1.0/24', gatewayIp: '10.20.1.1' }],
  assets: [
    {
      key: 'portal',
      name: 'Portal',
      kind: 0,
      runtimeResourceId: 'container-1',
      primaryIp: '10.20.1.10',
      status: 5,
      error: null,
    },
  ],
  createdAt: 1_790_000_000_000,
  updatedAt: 1_790_000_010_000,
  error: null,
}

const pathWire = {
  id: '019f0000-0000-7000-8000-000000000201',
  confidence: 0,
  sourceIp: '10.20.1.10',
  sourcePort: 41200,
  destinationIp: '10.30.1.20',
  destinationPort: 443,
  protocol: 'tcp',
  startedAt: 1_790_000_020_000,
  endedAt: 1_790_000_021_000,
  hops: [
    {
      ordinal: 0,
      observedAt: 1_790_000_020_000,
      evidenceKind: 0,
      observationPointKind: 0,
      shardId: runtimeWire.shards[0].id,
      networkKey: 'edge',
      infrastructureKey: null,
      assetKey: 'portal',
      direction: 'egress',
      sourceIp: '10.20.1.10',
      sourcePort: 41200,
      destinationIp: '10.30.1.20',
      destinationPort: 443,
      protocol: 'tcp',
    },
  ],
}

const captureWire = {
  id: '019f0000-0000-7000-8000-000000000301',
  status: 1,
  scope: 'network',
  networkKey: 'edge',
  maxBytes: 1048576,
  maxSeconds: 60,
  capturedBytes: 4096,
  createdAt: 1_790_000_030_000,
  startedAt: 1_790_000_031_000,
  completedAt: null,
  expiresAt: 1_790_003_600_000,
  segments: [
    {
      id: '019f0000-0000-7000-8000-000000000302',
      status: 1,
      observationPointId: '019f0000-0000-7000-8000-000000000303',
      observationPointKind: 0,
      networkKey: 'edge',
      infrastructureKey: null,
      assetKey: null,
      capturedBytes: 4096,
      uploadedBytes: 0,
      sha256: null,
      error: null,
    },
  ],
  error: null,
}

describe('TeamLab runtime contract boundary', () => {
  it('strictly parses runtime placement and semantic enums', () => {
    expect(parseTeamLabRuntime(runtimeWire)).toMatchObject({
      status: 'running',
      shards: [{ workerNodeName: 'worker-a', status: 'running' }],
      assets: [{ kind: 'docker', status: 'running' }],
    })
    expect(() => parseTeamLabRuntime({ ...runtimeWire, generation: '2' })).toThrow(TeamLabContractError)
  })

  it('strictly parses correlated paths and capture segments', () => {
    expect(parseTeamLabTrafficPath(pathWire)).toMatchObject({
      confidence: 'packet-exact',
      hops: [{ evidenceKind: 'packet', observationPointKind: 'network-bridge' }],
    })
    expect(parseTeamLabCapture(captureWire)).toMatchObject({
      status: 'running',
      segments: [{ status: 'running', observationPointKind: 'network-bridge' }],
    })
  })

  it('lists active access grants through the management projection', async () => {
    const grant = {
      id: '019f0000-0000-7000-8000-000000000401',
      type: 'WireGuard',
      clientAddress: '10.250.0.2/32',
      endpoint: 'vpn.example.test:51820',
      allowedIps: '10.20.1.0/24',
      dns: '10.20.1.1',
      createdAt: 1_790_000_040_000,
      expiresAt: 1_790_003_600_000,
      configurationDownloadUrl: null,
    }
    const get = vi.fn().mockResolvedValue([grant])
    const api = createTeamLabRuntimeApi(runtimeClient({ get }))

    await expect(api.listAccessGrants(runtimeWire.id)).resolves.toEqual([grant])
    expect(get).toHaveBeenCalledWith(`/api/admin/teamlab/runtimes/${runtimeWire.id}/access-grants`)
  })

  it('uses the complete admin runtime routes and preserves cursor semantics', async () => {
    const get = vi
      .fn()
      .mockResolvedValueOnce(runtimeWire)
      .mockResolvedValueOnce([
        {
          cursor: 12,
          generation: 2,
          stage: 'ready',
          level: 1,
          message: 'Runtime ready.',
          objectType: null,
          objectId: null,
          createdAt: 1_790_000_010_000,
        },
      ])
      .mockResolvedValueOnce({ items: [], nextCursor: null })
      .mockResolvedValueOnce({ items: [], nextCursor: 'path-next' })
      .mockResolvedValueOnce(pathWire)
      .mockResolvedValueOnce(captureWire)
    const api = createTeamLabRuntimeApi(runtimeClient({ get }))

    await api.getRuntime(runtimeWire.id)
    await api.listEvents(runtimeWire.id, 11, 50)
    await api.listFlows(runtimeWire.id, 'flow-cursor', 25)
    await api.listPaths(runtimeWire.id, 'path-cursor', 25)
    await api.getPath(runtimeWire.id, pathWire.id)
    await api.getCapture(runtimeWire.id, captureWire.id)

    expect(get.mock.calls).toEqual([
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}`],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/events`, { after: 11, limit: 50 }],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/traffic/flows`, { after: 'flow-cursor', limit: 25 }],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/traffic/paths`, { after: 'path-cursor', limit: 25 }],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/traffic/paths/${pathWire.id}`],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/captures/${captureWire.id}`],
    ])
  })

  it('serializes reset, access and capture mutations through runtimeJsonClient', async () => {
    const postJson = vi
      .fn()
      .mockResolvedValueOnce(runtimeWire)
      .mockResolvedValueOnce({
        id: '019f0000-0000-7000-8000-000000000401',
        type: 'WireGuard',
        clientAddress: '10.250.0.2/32',
        endpoint: 'vpn.example.test:51820',
        allowedIps: '10.20.1.0/24',
        dns: '10.20.1.1',
        createdAt: 1_790_000_040_000,
        expiresAt: null,
        configurationDownloadUrl: `/api/admin/teamlab/runtimes/${runtimeWire.id}/access-grants/grant/download?token=one-time`,
      })
      .mockResolvedValueOnce(captureWire)
      .mockResolvedValueOnce({ ...captureWire, status: 2 })
    const remove = vi.fn().mockResolvedValue(undefined)
    const api = createTeamLabRuntimeApi(runtimeClient({ postJson, delete: remove }))

    await api.resetRuntime(runtimeWire.id, { overlays: null, releaseId: null })
    const grant = await api.createAccessGrant(runtimeWire.id)
    await api.revokeAccessGrant(runtimeWire.id, grant.id)
    await api.startCapture(runtimeWire.id, {
      scope: 'network',
      networkKey: 'edge',
      maxSeconds: 60,
      maxBytes: 1048576,
      expiresInSeconds: 3600,
    })
    await api.stopCapture(runtimeWire.id, captureWire.id)

    expect(postJson.mock.calls).toEqual([
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/reset`, { overlays: null, releaseId: null }],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/access-grants`, { type: 'WireGuard' }],
      [
        `/api/admin/teamlab/runtimes/${runtimeWire.id}/captures`,
        { scope: 'network', networkKey: 'edge', maxSeconds: 60, maxBytes: 1048576, expiresInSeconds: 3600 },
      ],
      [`/api/admin/teamlab/runtimes/${runtimeWire.id}/captures/${captureWire.id}/stop`],
    ])
    expect(remove).toHaveBeenCalledWith(
      `/api/admin/teamlab/runtimes/${runtimeWire.id}/access-grants/${grant.id}`
    )
  })

  it('returns the server projection from DELETE and rejects trial creation without header transport support', async () => {
    const deleteJson = vi.fn().mockResolvedValue({ ...runtimeWire, status: 9 })
    const api = createTeamLabRuntimeApi({ ...runtimeClient(), deleteJson })

    await expect(api.destroyRuntime(runtimeWire.id)).resolves.toMatchObject({ status: 'destroying' })
    await expect(
      api.createTrial('trial-key', {
        releaseId: runtimeWire.releaseId,
        constraints: null,
        overlays: null,
        externalReference: null,
      })
    ).rejects.toMatchObject({ code: 'request_headers_unsupported' })
    expect(deleteJson).toHaveBeenCalledWith(`/api/admin/teamlab/runtimes/${runtimeWire.id}`)
  })

  it('sends trial creation with the required idempotency header when the transport supports it', async () => {
    const postJsonWithHeaders = vi.fn().mockResolvedValue(runtimeWire)
    const headerClient = {
      ...runtimeClient(),
      postJsonWithHeaders,
    }
    const api = createTeamLabRuntimeApi(headerClient)

    await expect(
      api.createTrial('trial-key', {
        releaseId: runtimeWire.releaseId,
        constraints: { preferredRegion: 'cn-test', requiredCapabilities: ['docker'] },
        overlays: [],
        externalReference: 'acceptance-1',
      })
    ).resolves.toMatchObject({ id: runtimeWire.id, status: 'running' })

    expect(postJsonWithHeaders).toHaveBeenCalledWith(
      '/api/admin/teamlab/runtimes/trials',
      {
        releaseId: runtimeWire.releaseId,
        constraints: { preferredRegion: 'cn-test', requiredCapabilities: ['docker'] },
        overlays: [],
        externalReference: 'acceptance-1',
      },
      { 'Idempotency-Key': 'trial-key' }
    )
  })

  it('exposes same-origin download paths without issuing JSON requests', () => {
    const api = createTeamLabRuntimeApi(runtimeClient())
    expect(api.captureDownloadPath(runtimeWire.id, captureWire.id)).toBe(
      `/api/admin/teamlab/runtimes/${runtimeWire.id}/captures/${captureWire.id}/download`
    )
    expect(api.accessGrantDownloadPath(runtimeWire.id, 'grant', 'one time')).toBe(
      `/api/admin/teamlab/runtimes/${runtimeWire.id}/access-grants/grant/download?token=one%20time`
    )
  })

  it('loads runtime-scoped logs from the TeamLab management contract', async () => {
    const get = vi.fn().mockResolvedValue({ items: [], nextCursor: null })
    const api = createTeamLabRuntimeApi({ ...runtimeClient(), get })

    await expect(api.listLogs(runtimeWire.id, null, 80)).resolves.toMatchObject({ items: [], nextCursor: null })
    expect(get).toHaveBeenCalledWith(`/api/admin/teamlab/runtimes/${runtimeWire.id}/logs`, {
      cursor: null,
      count: 80,
    })
  })
})
