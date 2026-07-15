import { describe, expect, it, vi } from 'vitest'
import { createImageTemplateAdminApi } from './imageTemplateAdminApi'
import { createInstanceAdminApi } from './instanceAdminApi'
import { createNodeAdminApi } from './nodeAdminApi'
import type { NodeResourcePage, NodeSummary } from './contracts'
import type { RuntimeJsonClient } from './runtimeJsonClient'

function createClient(get: RuntimeJsonClient['get']): RuntimeJsonClient {
  const unexpected = async () => {
    throw new Error('Unexpected API call')
  }
  return {
    get,
    postJson: unexpected,
    postForm: unexpected,
    patchJson: unexpected,
    delete: unexpected,
  }
}

describe('imageTemplateAdminApi', () => {
  it('accepts the deployed image page shape', async () => {
    const item = {
      id: 118,
      name: 'TeamLab Docker Chain HTTP Acceptance 2',
      osType: 0,
      imageType: 0,
      fileSize: 0,
      status: 0,
      description: null,
      errorMessage: null,
      imageHash: null,
      uploadedAt: 1_783_327_777_739,
      registryUrl: 'gzctf-internal://teamlab/chain-http:20260706',
    }
    const get = vi.fn().mockResolvedValue({ total: 103, page: 1, pageSize: 20, items: [item] })
    const adapter = createImageTemplateAdminApi(createClient(get))

    await expect(adapter.list()).resolves.toEqual({ total: 103, page: 1, pageSize: 20, items: [item] })
  })
})

describe('nodeAdminApi', () => {
  const node = {
    id: 'c08073af-56d7-4b54-b338-04f64ac92bd0',
    name: 'worker-10.24.0.31',
    hostAddress: '10.24.0.31',
    status: 1,
    capabilities: 3,
    cpuLoad: 0.18,
    memoryLoad: 0.32,
    currentContainers: 0,
    maxContainers: 1,
    reservedContainers: 0,
    allocatedContainers: 0,
    currentVms: 1,
    maxVms: 5,
    reservedVms: 0,
    allocatedVms: 1,
    usedPorts: 1,
    totalPorts: 60,
    portPoolStart: 30000,
    portPoolEnd: 30059,
    portPoolMode: 'nginx',
    lastHeartbeat: 1_784_082_368_817,
    isSchedulable: true,
    isLocal: false,
    agentPort: 5001,
    teamLabNetworkEnabled: true,
    teamLabTunnelStatus: 3,
    teamLabTunnelIp: '10.24.0.31',
    teamLabTunnelLastHandshake: 1_783_500_724_849,
    teamLabTunnelLastError: null,
    teamLabTunnelConfigVersion: 27,
    teamLabAgentVersion: '1.0.0.0',
    teamLabProtocolVersion: 3,
    teamLabFabricIp: null,
    teamLabFabricStatus: 0,
    teamLabCapabilitiesJson: '{"docker":true,"kvm":true}',
    canHostTeamLab: true,
    canHostTeamLabFabric: true,
    canHostTeamLabDocker: true,
    canHostTeamLabVm: true,
    unschedulableReasons: [],
    unschedulableByCapability: {
      docker: null,
      kvm: null,
      teamLabNetwork: null,
      teamLabDocker: null,
      teamLabVm: null,
    },
    schedulableCapabilities: ['Docker', 'Kvm', 'TeamLabNetwork', 'TeamLabDocker', 'TeamLabVm'],
  }

  it('accepts the deployed node list shape', async () => {
    const adapter = createNodeAdminApi(createClient(vi.fn().mockResolvedValue([node])))
    await expect(adapter.list()).resolves.toEqual([node])
  })

  it('accepts the deployed mixed resource page shape', async () => {
    const resource = {
      kind: 'vm',
      id: 'd64ddc0c-3a13-4cad-a902-5faf6a0ad119',
      name: 'vm_c40_admin',
      status: 'Running',
      isActive: true,
      startedAt: 1_783_787_308_260,
      expectedStopAt: null,
      stoppedAt: null,
      duration: '3天 9小时',
      image: null,
      runtimeId: 'vm_c40_admin',
      entry: 'https://example.test/guacamole',
      ip: '192.168.122.44',
      port: null,
      gameId: 23,
      gameTitle: 'CTF题库',
      challengeId: 40,
      challengeTitle: 'windows应急响应',
      challengeCategory: 'IR',
      teamId: 1,
      teamName: 'admin',
      userId: '019e5fa3-34ba-7697-8949-cfa343cde908',
      userName: 'admin',
      providerName: 'KVM',
      osType: 'Windows',
    }
    const page = {
      nodeId: node.id,
      nodeName: node.name,
      page: 1,
      pageSize: 12,
      total: 1,
      runningCount: 1,
      containerCount: 0,
      vmCount: 1,
      pentestCount: 0,
      teamLabCount: 0,
      items: [resource],
    }
    const adapter = createNodeAdminApi(createClient(vi.fn().mockResolvedValue(page)))

    await expect(adapter.resources(node.id)).resolves.toEqual(page)
  })
})

describe('instanceAdminApi', () => {
  const nodes = [
    { id: 'node-1', name: 'worker-1' },
    { id: 'node-2', name: 'worker-2' },
  ] as NodeSummary[]
  const resource = {
    kind: 'container',
    id: 'container-1',
    name: 'SSTI',
    status: 'Running',
    isActive: true,
    startedAt: 1_784_082_368_817,
    expectedStopAt: null,
    stoppedAt: null,
    duration: '1分钟',
    image: 'registry/ssti:latest',
    runtimeId: 'abc123',
    entry: 'http://example.test:32768',
    ip: '10.24.0.30',
    port: 32768,
    gameId: 23,
    gameTitle: 'CTF题库',
    challengeId: 19,
    challengeTitle: 'SSTI',
    challengeCategory: 'Web',
    teamId: 1,
    teamName: 'admin',
    userId: null,
    userName: null,
    providerName: 'Docker',
    osType: 'Linux',
  }

  function page(nodeId: string): NodeResourcePage {
    return {
      nodeId,
      nodeName: nodeId,
      page: 1,
      pageSize: 50,
      total: 1,
      runningCount: 1,
      containerCount: 1,
      vmCount: 0,
      pentestCount: 0,
      teamLabCount: 0,
      items: [{ ...resource, id: `container-${nodeId}` }],
    }
  }

  it('aggregates node resources without mixing in legacy containers', async () => {
    const resources = vi.fn(async (nodeId: string) => page(nodeId))
    const adapter = createInstanceAdminApi(createClient(vi.fn()), { resources })

    const inventory = await adapter.inventory(nodes, 'active')

    expect(inventory.source).toBe('node-resources')
    expect(inventory.items).toHaveLength(2)
    expect(inventory.loadedNodes).toBe(2)
    expect(inventory.items[0]).toHaveProperty('nodeName')
  })

  it('keeps healthy node results when one node fails', async () => {
    const resources = vi.fn(async (nodeId: string) => {
      if (nodeId === 'node-2') throw new Error('node offline')
      return page(nodeId)
    })
    const adapter = createInstanceAdminApi(createClient(vi.fn()), { resources })

    const inventory = await adapter.inventory(nodes, 'active')

    expect(inventory.source).toBe('node-resources')
    expect(inventory.items).toHaveLength(1)
    expect(inventory.loadedNodes).toBe(1)
    expect(inventory.failures).toEqual([{ nodeId: 'node-2', nodeName: 'worker-2', message: 'node offline' }])
  })

  it('uses the explicitly labelled legacy view only when every node resource request fails', async () => {
    const get = vi.fn().mockResolvedValue({
      data: [
        {
          containerGuid: 'legacy-1',
          challenge: { id: 19, title: 'SSTI', category: 'Web' },
          team: { id: 1, name: 'admin' },
          startedAt: 1_784_082_368_817,
          ip: '10.24.0.30',
          port: 32768,
        },
      ],
      length: 1,
      total: 1,
    })
    const resources = vi.fn().mockRejectedValue(new Error('resource endpoint unavailable'))
    const adapter = createInstanceAdminApi(createClient(get), { resources })

    const inventory = await adapter.inventory(nodes, 'active')

    expect(inventory.source).toBe('legacy-containers')
    expect(inventory.items).toHaveLength(1)
    expect(inventory.items[0].nodeName).toBe('传统容器接口')
    expect(get).toHaveBeenCalledWith('/api/admin/instances')
  })

  it('never substitutes active legacy containers for a failed history query', async () => {
    const get = vi.fn()
    const resources = vi.fn().mockRejectedValue(new Error('history endpoint unavailable'))
    const adapter = createInstanceAdminApi(createClient(get), { resources })

    const inventory = await adapter.inventory(nodes, 'history', 'node-1')

    expect(inventory.source).toBe('node-resources')
    expect(inventory.items).toEqual([])
    expect(inventory.failures).toHaveLength(1)
    expect(get).not.toHaveBeenCalled()
  })
})
